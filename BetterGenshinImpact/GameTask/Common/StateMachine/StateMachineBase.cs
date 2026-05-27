using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Common.StateMachine;

using static TaskControl;

/// <summary>
/// 状态处理器返回结果，决定状态机如何处理当前状态
/// </summary>
public enum StateHandlerResult
{
    /// <summary>
    /// 成功：操作完成，状态机自动等待邻接状态转换；转场超时会占用当前状态的重试次数
    /// </summary>
    Success,

    /// <summary>
    /// 等待：可预期的等待，状态机继续循环重新检测
    /// </summary>
    Wait,

    /// <summary>
    /// 重试：意外失败，状态机重试直到超过最大次数
    /// </summary>
    Retry,

    /// <summary>
    /// 失败：无法恢复的错误，状态机抛出异常
    /// </summary>
    Fail
}

/// <summary>
/// 状态处理器委托
/// </summary>
public delegate Task<StateHandlerResult> StateHandlerDelegate<in TContext>(TContext context);

/// <summary>
/// 状态检测器标记。检测器方法签名必须为 bool Method(ImageRegion ra)。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StateDetectorAttribute(object state) : Attribute
{
    public object State { get; } = state;

    /// <summary>
    /// 检测顺序，数值越小越先检测。
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// 状态处理器标记。处理器方法签名必须为 Task&lt;StateHandlerResult&gt; Method(TContext context)。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StateHandlerAttribute(object state) : Attribute
{
    public object State { get; } = state;

    /// <summary>
    /// 当前状态的重试次数上限，未设置时使用状态机默认值。
    /// </summary>
    public int RetryTimes { get; set; } = -1;

    /// <summary>
    /// 当前状态的重试时间上限（毫秒），与 RetryTimes 互斥。
    /// </summary>
    public int RetryTimeout { get; set; } = -1;

    /// <summary>
    /// 当前状态触发重试后的下一轮循环间隔（毫秒）。
    /// </summary>
    public int RetryInterval { get; set; } = -1;

    /// <summary>
    /// 当前状态 Handler 返回 Success 后等待邻接状态转换的超时时间（毫秒）。
    /// </summary>
    public int TransitionTimeout { get; set; } = -1;
}

/// <summary>
/// 未知状态处理器标记。处理器方法签名必须为 Task&lt;StateHandlerResult&gt; Method(TContext context)。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnknownStateHandlerAttribute : Attribute
{
}

/// <summary>
/// 有限状态机基类
/// 
/// 为什么使用状态机：
/// - 游戏自动化涉及大量界面切换，传统的 if-else 难以维护
/// - 状态机将每个界面抽象为状态，状态转换关系清晰可追踪
/// - 闭环确认机制确保操作真正生效，而不是盲目点击
/// 
/// 为什么使用注册模式：
/// - 将"状态图"与"状态处理逻辑"分离，便于修改和调试
/// - 状态图在一处定义，避免分散在代码各处难以维护
/// - 检测器注册使状态机能自动选择最优检测策略
/// 
/// 为什么 Handler 返回 StateHandlerResult：
/// - 状态机需要知道操作结果来决定下一步行为
/// - Success/Wait/Retry/Fail 四种结果覆盖所有场景
/// - 状态机统一处理等待和重试逻辑，Handler 只关注操作本身
/// 
/// 详细使用文档见 StateMachine/README.md
/// </summary>
public abstract class StateMachineBase<TState, TContext> where TState : struct, Enum
{
    /// <summary>
    /// 由子类实现以提供 Logger
    /// </summary>
    protected abstract ILogger Logger { get; }

    protected CancellationToken _ct;

    /// <summary>
    /// 当前状态
    /// </summary>
    public TState CurrentState { get; protected set; }

    /// <summary>
    /// 当前状态已累计的重试次数。首次执行当前状态 Handler 时为 0。
    /// </summary>
    protected int CurrentStateRetryCount { get; private set; }

    /// <summary>
    /// 当前状态的重试次数上限。使用时间策略时为 0。
    /// </summary>
    protected int CurrentStateRetryLimit { get; private set; }

    /// <summary>
    /// 当前状态的重试超时时间（毫秒）。为 null 时表示当前状态使用次数上限。
    /// </summary>
    protected int? CurrentStateRetryTimeout { get; private set; }

    /// <summary>
    /// 当前状态是否使用重试超时策略。
    /// </summary>
    protected bool CurrentStateRetryUsesTimeout => CurrentStateRetryTimeout.HasValue;

    /// <summary>
    /// 当前状态触发重试后的下一轮循环间隔（毫秒）。
    /// </summary>
    protected int CurrentStateRetryInterval { get; private set; }

    /// <summary>
    /// 当前状态 Handler 返回 Success 后等待邻接状态转换的超时时间（毫秒）。
    /// </summary>
    protected int CurrentStateTransitionTimeout { get; private set; }

    /// <summary>
    /// 状态处理器注册表
    /// </summary>
    private readonly Dictionary<TState, StateHandlerDelegate<TContext>> _stateHandlers = new();

    /// <summary>
    /// 状态检测器注册表 - 每个状态对应一个检测函数
    /// 检测函数接收截图，返回 true 表示匹配
    /// </summary>
    private readonly Dictionary<TState, Func<ImageRegion, bool>> _stateDetectors = new();

    /// <summary>
    /// 状态邻接关系表 - 记录每个状态可能转换到的下一个状态
    /// 用于优化状态检测：只检测当前状态可能到达的状态
    /// </summary>
    private readonly Dictionary<TState, TState[]> _stateTransitions = new();

    /// <summary>
    /// 状态重试上限表 - 未注册时使用运行状态机传入的默认重试次数
    /// </summary>
    private readonly Dictionary<TState, int> _stateRetryLimits = new();

    /// <summary>
    /// 状态重试超时表 - 注册后与重试次数上限互斥
    /// </summary>
    private readonly Dictionary<TState, int> _stateRetryTimeouts = new();

    /// <summary>
    /// 状态重试间隔表 - 未注册时使用状态机循环间隔
    /// </summary>
    private readonly Dictionary<TState, int> _stateRetryIntervals = new();

    /// <summary>
    /// 状态转换超时表 - 未注册时使用默认状态转换超时
    /// </summary>
    private readonly Dictionary<TState, int> _stateTransitionTimeouts = new();

    /// <summary>
    /// 未知状态处理器
    /// </summary>
    private StateHandlerDelegate<TContext>? _unknownStateHandler;

    /// <summary>
    /// 默认状态检测间隔（毫秒）
    /// </summary>
    protected virtual int DefaultDetectionInterval => 300;

    /// <summary>
    /// 默认状态转换超时（毫秒）
    /// </summary>
    protected virtual int DefaultTransitionTimeout => 3000;

    /// <summary>
    /// 默认中间态转换超时（毫秒）
    /// </summary>
    protected virtual int DefaultIntermediateTransitionTimeout => 120000;

    /// <summary>
    /// 默认最大重试次数
    /// </summary>
    protected virtual int DefaultMaxRetries => 3;

    /// <summary>
    /// 状态机循环间隔（毫秒）
    /// </summary>
    protected virtual int StateMachineLoopInterval => 200;

    #region 状态处理器注册

    /// <summary>
    /// 注册状态处理器
    /// </summary>
    /// <param name="state">状态</param>
    /// <param name="handler">处理器</param>
    protected void RegisterStateHandler(TState state, StateHandlerDelegate<TContext> handler)
    {
        _stateHandlers[state] = handler;
    }

    /// <summary>
    /// 注册多个状态处理器
    /// </summary>
    protected void RegisterStateHandlers(params (TState state, StateHandlerDelegate<TContext> handler)[] handlers)
    {
        foreach (var (state, handler) in handlers)
        {
            _stateHandlers[state] = handler;
        }
    }

    /// <summary>
    /// 注册未知状态处理器（可选）
    /// 当检测到 Unknown 状态或未注册处理器的状态时调用
    /// </summary>
    protected void RegisterUnknownStateHandler(StateHandlerDelegate<TContext> handler)
    {
        _unknownStateHandler = handler;
    }

    /// <summary>
    /// 扫描当前任务类中带 Attribute 的方法，自动注册状态检测器、处理器和未知状态处理器。
    /// </summary>
    protected void RegisterStateMethodsByAttribute()
    {
        var methods = GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        var detectorStates = new HashSet<TState>();
        var detectors = methods
            .Select(method => (method, attribute: method.GetCustomAttribute<StateDetectorAttribute>()))
            .Where(item => item.attribute != null)
            .OrderBy(item => item.attribute!.Order)
            .ThenBy(item => item.method.Name, StringComparer.Ordinal);

        foreach (var (method, attribute) in detectors)
        {
            var state = ConvertAttributeState(attribute!.State, method);
            if (!detectorStates.Add(state))
            {
                throw new InvalidOperationException($"状态 {state} 注册了多个检测器");
            }

            ValidateDetectorMethod(method);
            RegisterStateDetector(state, (Func<ImageRegion, bool>)method.CreateDelegate(typeof(Func<ImageRegion, bool>), this));
        }

        var handlerStates = new HashSet<TState>();
        var handlers = methods
            .Select(method => (method, attribute: method.GetCustomAttribute<StateHandlerAttribute>()))
            .Where(item => item.attribute != null)
            .OrderBy(item => item.method.Name, StringComparer.Ordinal);

        foreach (var (method, attribute) in handlers)
        {
            var state = ConvertAttributeState(attribute!.State, method);
            if (!handlerStates.Add(state))
            {
                throw new InvalidOperationException($"状态 {state} 注册了多个处理器");
            }

            ValidateHandlerMethod(method);
            RegisterStateHandler(state, (StateHandlerDelegate<TContext>)method.CreateDelegate(typeof(StateHandlerDelegate<TContext>), this));
            RegisterHandlerOptions(state, attribute, method);
        }

        var unknownHandlers = methods
            .Where(method => method.GetCustomAttribute<UnknownStateHandlerAttribute>() != null)
            .ToArray();
        if (unknownHandlers.Length > 1)
        {
            throw new InvalidOperationException("未知状态处理器只能注册一个");
        }

        if (unknownHandlers.Length == 1)
        {
            var method = unknownHandlers[0];
            ValidateHandlerMethod(method);
            RegisterUnknownStateHandler((StateHandlerDelegate<TContext>)method.CreateDelegate(typeof(StateHandlerDelegate<TContext>), this));
        }
    }

    private static TState ConvertAttributeState(object state, MethodInfo method)
    {
        if (state is TState typedState)
        {
            return typedState;
        }

        if (state == null)
        {
            throw new InvalidOperationException($"方法 {method.Name} 的状态不能为空");
        }

        var stateTypeName = state.GetType().Name;
        throw new InvalidOperationException($"方法 {method.Name} 的状态类型 {stateTypeName} 与状态机状态类型 {typeof(TState).Name} 不匹配");
    }

    private static void ValidateDetectorMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (method.ReturnType != typeof(bool) ||
            parameters.Length != 1 ||
            parameters[0].ParameterType != typeof(ImageRegion))
        {
            throw new InvalidOperationException($"状态检测器 {method.Name} 的签名必须是 bool {method.Name}(ImageRegion ra)");
        }
    }

    private static void ValidateHandlerMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (method.ReturnType != typeof(Task<StateHandlerResult>) ||
            parameters.Length != 1 ||
            parameters[0].ParameterType != typeof(TContext))
        {
            throw new InvalidOperationException($"状态处理器 {method.Name} 的签名必须是 Task<StateHandlerResult> {method.Name}({typeof(TContext).Name} context)");
        }
    }

    private void RegisterHandlerOptions(TState state, StateHandlerAttribute attribute, MethodInfo method)
    {
        if (attribute.RetryTimes == 0)
        {
            throw new InvalidOperationException($"状态处理器 {method.Name} 的 RetryTimes 必须大于 0");
        }

        if (attribute.RetryTimeout == 0)
        {
            throw new InvalidOperationException($"状态处理器 {method.Name} 的 RetryTimeout 必须大于 0");
        }

        if (attribute.RetryTimes > 0 && attribute.RetryTimeout > 0)
        {
            throw new InvalidOperationException($"状态处理器 {method.Name} 不能同时设置 RetryTimes 和 RetryTimeout");
        }

        if (attribute.RetryTimes > 0)
        {
            RegisterStateRetryLimit(state, attribute.RetryTimes);
        }

        if (attribute.RetryTimeout > 0)
        {
            RegisterStateRetryTimeout(state, attribute.RetryTimeout);
        }

        if (attribute.RetryInterval == 0)
        {
            throw new InvalidOperationException($"状态处理器 {method.Name} 的 RetryInterval 必须大于 0");
        }

        if (attribute.RetryInterval > 0)
        {
            RegisterStateRetryInterval(state, attribute.RetryInterval);
        }

        if (attribute.TransitionTimeout == 0)
        {
            throw new InvalidOperationException($"状态处理器 {method.Name} 的 TransitionTimeout 必须大于 0");
        }

        if (attribute.TransitionTimeout > 0)
        {
            RegisterStateTransitionTimeout(state, attribute.TransitionTimeout);
        }
    }

    #endregion

    #region 状态转换注册【推荐 - 有限状态机核心】

    /// <summary>
    /// 注册状态转换关系【推荐】
    /// 这是有限状态机的核心 - 定义每个状态可能转换到的下一个状态
    /// 
    /// 优势：
    /// 1. 状态检测只检测邻接状态，大幅提升性能
    /// 2. 状态流程清晰可见，便于维护
    /// 3. 自动排除不可能的状态转换
    /// </summary>
    /// <param name="fromState">源状态</param>
    /// <param name="toStates">可能的目标状态数组（邻接状态）</param>
    protected void RegisterStateTransition(TState fromState, params TState[] toStates)
    {
        ValidateStateTransition(fromState, toStates);
        _stateTransitions[fromState] = toStates;
    }

    /// <summary>
    /// 批量注册状态转换关系【推荐】
    /// </summary>
    protected void RegisterStateTransitions(params (TState from, TState[] to)[] transitions)
    {
        foreach (var (from, to) in transitions)
        {
            RegisterStateTransition(from, to);
        }
    }

    private static void ValidateStateTransition(TState fromState, TState[] toStates)
    {
        if (toStates == null || toStates.Length == 0)
        {
            throw new InvalidOperationException($"状态 {fromState} 必须至少注册一个邻接状态；如果这是结束节点，请不要注册状态转移");
        }

        if (toStates.Any(state => EqualityComparer<TState>.Default.Equals(state, fromState)))
        {
            throw new InvalidOperationException($"状态 {fromState} 的邻接状态不能包含自身");
        }
    }

    /// <summary>
    /// 获取当前状态可能转换到的状态列表
    /// 如果未注册，返回 null（表示需要检测所有状态）
    /// </summary>
    protected TState[]? GetPossibleNextStates()
    {
        return _stateTransitions.TryGetValue(CurrentState, out var states) ? states : null;
    }

    #endregion

    #region 状态重试上限注册

    /// <summary>
    /// 注册指定状态的重试上限，用于覆盖状态机运行时传入的默认重试次数
    /// </summary>
    protected void RegisterStateRetryLimit(TState state, int maxRetries)
    {
        if (maxRetries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "状态重试上限必须大于 0");
        }

        _stateRetryLimits[state] = maxRetries;
        _stateRetryTimeouts.Remove(state);
    }

    /// <summary>
    /// 批量注册指定状态的重试上限
    /// </summary>
    protected void RegisterStateRetryLimits(params (TState state, int maxRetries)[] retryLimits)
    {
        foreach (var (state, maxRetries) in retryLimits)
        {
            RegisterStateRetryLimit(state, maxRetries);
        }
    }

    private int GetMaxRetriesForState(TState state, int defaultMaxRetries)
    {
        return _stateRetryLimits.TryGetValue(state, out var maxRetries) ? maxRetries : defaultMaxRetries;
    }

    /// <summary>
    /// 注册指定状态的重试超时时间，用于替代次数上限。
    /// </summary>
    protected void RegisterStateRetryTimeout(TState state, int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "状态重试超时时间必须大于 0");
        }

        _stateRetryTimeouts[state] = timeoutMs;
        _stateRetryLimits.Remove(state);
    }

    /// <summary>
    /// 批量注册指定状态的重试超时时间
    /// </summary>
    protected void RegisterStateRetryTimeouts(params (TState state, int timeoutMs)[] retryTimeouts)
    {
        foreach (var (state, timeoutMs) in retryTimeouts)
        {
            RegisterStateRetryTimeout(state, timeoutMs);
        }
    }

    /// <summary>
    /// 注册指定状态触发重试后的下一轮循环间隔，未注册时使用 StateMachineLoopInterval。
    /// </summary>
    protected void RegisterStateRetryInterval(TState state, int intervalMs)
    {
        if (intervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "状态重试间隔必须大于 0");
        }

        _stateRetryIntervals[state] = intervalMs;
    }

    /// <summary>
    /// 注册指定状态 Handler 返回 Success 后等待邻接状态转换的超时时间。
    /// </summary>
    protected void RegisterStateTransitionTimeout(TState state, int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "状态转换超时时间必须大于 0");
        }

        _stateTransitionTimeouts[state] = timeoutMs;
    }

    private (bool useTimeout, int value) GetRetryPolicyForState(TState state, int defaultMaxRetries)
    {
        if (_stateRetryTimeouts.TryGetValue(state, out var timeoutMs))
        {
            return (true, timeoutMs);
        }

        return (false, GetMaxRetriesForState(state, defaultMaxRetries));
    }

    private int GetRetryIntervalForState(TState state)
    {
        return _stateRetryIntervals.TryGetValue(state, out var intervalMs) ? intervalMs : StateMachineLoopInterval;
    }

    private int GetTransitionTimeoutForState(TState state)
    {
        return _stateTransitionTimeouts.TryGetValue(state, out var timeoutMs) ? timeoutMs : DefaultTransitionTimeout;
    }

    private void RecordStateRetry(string reason, (bool useTimeout, int value) retryPolicy, Stopwatch stateRetryStopwatch)
    {
        CurrentStateRetryCount++;

        if (retryPolicy.useTimeout)
        {
            var elapsedMilliseconds = stateRetryStopwatch.ElapsedMilliseconds;
            if (elapsedMilliseconds >= retryPolicy.value)
            {
                Logger.LogError("状态 {State} {Reason}，重试超时达到上限 {Max} ms，转为失败",
                    CurrentState, reason, retryPolicy.value);
                throw new InvalidOperationException($"状态 {CurrentState} 重试超时达到上限 {retryPolicy.value} ms");
            }

            Logger.LogWarning("状态 {State} {Reason}，将在重试超时窗口内继续重试（第 {Count} 次，已用 {Elapsed}/{Max} ms）",
                CurrentState, reason, CurrentStateRetryCount, elapsedMilliseconds, retryPolicy.value);
            return;
        }

        if (CurrentStateRetryCount >= retryPolicy.value)
        {
            Logger.LogError("状态 {State} {Reason}，重试次数达到上限 {Max}，转为失败",
                CurrentState, reason, retryPolicy.value);
            throw new InvalidOperationException($"状态 {CurrentState} 重试次数达到上限 {retryPolicy.value}");
        }

        Logger.LogWarning("状态 {State} {Reason}，将重试当前状态（第 {Count}/{Max} 次）",
            CurrentState, reason, CurrentStateRetryCount, retryPolicy.value);
    }

    #endregion

    #region 状态机运行

    /// <summary>
    /// 初始化状态机
    /// </summary>
    protected void Initialize(CancellationToken ct, TState initialState = default)
    {
        _ct = ct;
        CurrentState = initialState;
    }

    /// <summary>
    /// 运行状态机直到达到任一目标状态
    /// 这是核心方法，封装了 switch-case 逻辑
    /// </summary>
    /// <param name="context">上下文对象</param>
    /// <param name="targetStates">目标状态（到达任一即停止）</param>
    /// <param name="maxIterations">最大迭代次数（防止无限循环）</param>
    /// <param name="maxRetries">默认单个状态最大重试次数，可通过 RegisterStateRetryLimit 覆盖指定状态</param>
    /// <exception cref="InvalidOperationException">Handler 返回 Fail、重试超限、转场超时超限或达到最大迭代次数时抛出</exception>
    protected async Task RunStateMachineUntil(TContext context, TState[] targetStates, int maxIterations = 1000, int maxRetries = 5)
    {
        Logger.LogInformation("========== 状态机启动，目标状态：{Targets} ==========",
            string.Join(", ", targetStates));

        CurrentStateRetryCount = 0;
        CurrentStateRetryLimit = maxRetries;
        CurrentStateRetryTimeout = null;
        CurrentStateRetryInterval = StateMachineLoopInterval;
        CurrentStateTransitionTimeout = DefaultTransitionTimeout;
        TState lastRetryState = default;
        var stateRetryStopwatch = Stopwatch.StartNew();

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            _ct.ThrowIfCancellationRequested();

            // 检测当前实际状态
            RefreshCurrentState();

            // 状态变化时重置重试计数
            if (!EqualityComparer<TState>.Default.Equals(CurrentState, lastRetryState))
            {
                CurrentStateRetryCount = 0;
                lastRetryState = CurrentState;
                stateRetryStopwatch.Restart();
            }

            // 检查是否到达目标状态
            if (targetStates.Contains(CurrentState))
            {
                Logger.LogInformation("========== 状态机完成，到达目标状态：{State} ==========", CurrentState);
                return;
            }

            Logger.LogDebug("状态机迭代 {Iteration}，当前状态：{State}", iteration, CurrentState);
            var retryPolicy = GetRetryPolicyForState(CurrentState, maxRetries);
            var retryInterval = GetRetryIntervalForState(CurrentState);
            var transitionTimeout = GetTransitionTimeoutForState(CurrentState);
            CurrentStateRetryLimit = retryPolicy.useTimeout ? 0 : retryPolicy.value;
            CurrentStateRetryTimeout = retryPolicy.useTimeout ? retryPolicy.value : null;
            CurrentStateRetryInterval = retryInterval;
            CurrentStateTransitionTimeout = transitionTimeout;
            var nextLoopInterval = StateMachineLoopInterval;

            // 执行状态处理器
            if (_stateHandlers.TryGetValue(CurrentState, out var handler))
            {
                var result = await handler(context);

                switch (result)
                {
                    case StateHandlerResult.Success:
                        // 成功：等待邻接状态转换；转场超时视为当前状态的一次重试
                        var nextState = await EnsureNextStateTransition(transitionTimeout);
                        if (EqualityComparer<TState>.Default.Equals(nextState, default))
                        {
                            RecordStateRetry("等待邻接状态转换超时", retryPolicy, stateRetryStopwatch);
                            nextLoopInterval = retryInterval;
                        }
                        else
                        {
                            CurrentStateRetryCount = 0; // 成功转换后重置重试计数
                            stateRetryStopwatch.Restart();
                        }
                        break;

                    case StateHandlerResult.Wait:
                        // 可预期的等待：继续循环，重新检测状态
                        Logger.LogDebug("Handler 返回 Wait，状态机继续等待");
                        break;

                    case StateHandlerResult.Retry:
                        // 意外失败重试：检查重试次数
                        RecordStateRetry("Handler 返回 Retry", retryPolicy, stateRetryStopwatch);
                        nextLoopInterval = retryInterval;
                        break;

                    case StateHandlerResult.Fail:
                        // 失败：抛出异常
                        throw new InvalidOperationException($"状态 {CurrentState} 的 Handler 返回失败");
                }
            }
            else if (!EqualityComparer<TState>.Default.Equals(CurrentState, default) && _unknownStateHandler != null)
            {
                Logger.LogWarning("状态 {State} 无注册处理器，使用未知状态处理器", CurrentState);
                await _unknownStateHandler(context);
            }
            else if (_unknownStateHandler != null)
            {
                await _unknownStateHandler(context);
            }
            else
            {
                Logger.LogWarning("状态 {State} 无注册处理器，跳过", CurrentState);
            }

            await Delay(nextLoopInterval, _ct);
        }

        Logger.LogError("状态机达到最大迭代次数 {Max}，目标状态：{Targets}，当前状态：{State}",
            maxIterations, string.Join(", ", targetStates), CurrentState);
        throw new InvalidOperationException($"状态机达到最大迭代次数 {maxIterations}，未到达目标状态：{string.Join(", ", targetStates)}");
    }

    /// <summary>
    /// 运行状态机直到达到单个目标状态
    /// </summary>
    protected Task RunStateMachineUntil(TContext context, TState targetState, int maxIterations = 1000)
    {
        return RunStateMachineUntil(context, new[] { targetState }, maxIterations);
    }

    #endregion

    #region 状态检测器注册

    /// <summary>
    /// 注册状态检测器
    /// 检测器接收截图区域，返回 true 表示当前处于该状态
    /// </summary>
    /// <param name="state">状态</param>
    /// <param name="detector">检测函数，接收 ImageRegion，返回 true 表示匹配</param>
    protected void RegisterStateDetector(TState state, Func<ImageRegion, bool> detector)
    {
        _stateDetectors[state] = detector;
    }

    /// <summary>
    /// 批量注册状态检测器【推荐】
    /// 注意：检测顺序影响性能，建议将快速检测的状态放在前面
    /// </summary>
    protected void RegisterStateDetectors(params (TState state, Func<ImageRegion, bool> detector)[] detectors)
    {
        foreach (var (state, detector) in detectors)
        {
            _stateDetectors[state] = detector;
        }
    }

    #endregion

    #region 核心状态检测方法

    /// <summary>
    /// 检测当前UI状态（完整检测）
    /// 基于注册的检测器，遍历所有注册的状态检测器
    /// 只截图一次，复用给所有检测器
    /// </summary>
    /// <returns>检测到的状态，如果都不匹配返回 default</returns>
    protected TState DetectCurrentState()
    {
        using var ra = CaptureToRectArea();
        foreach (var (state, detector) in _stateDetectors)
        {
            if (detector(ra))
            {
                return state;
            }
        }
        return default;
    }

    /// <summary>
    /// 在候选状态中检测当前状态
    /// 只检测 candidateStates 中的状态，跳过其他状态
    /// 只截图一次，复用给所有检测器
    /// </summary>
    /// <param name="candidateStates">候选状态列表</param>
    /// <returns>检测到的状态，如果不在候选列表中则返回 default</returns>
    protected TState DetectStateAmong(TState[] candidateStates)
    {
        using var ra = CaptureToRectArea();
        foreach (var state in candidateStates)
        {
            if (_stateDetectors.TryGetValue(state, out var detector) && detector(ra))
            {
                return state;
            }
        }
        return default;
    }

    /// <summary>
    /// 更新当前状态（通过检测）
    /// 严格模式：只检测邻接状态，不回退到全状态检测
    /// </summary>
    protected void RefreshCurrentState()
    {
        // 如果当前状态有注册邻接关系，只检测可能的下一个状态
        var possibleStates = GetPossibleNextStates();
        TState detected;

        if (possibleStates != null && possibleStates.Length > 0)
        {
            // 严格 FSM：只检测邻接状态
            detected = DetectStateAmong(possibleStates);
        }
        else
        {
            // 没有邻接状态定义时（如初始状态），回退到全状态检测
            detected = DetectCurrentState();
        }

        if (!EqualityComparer<TState>.Default.Equals(detected, default))
        {
            CurrentState = detected;
        }
    }

    /// <summary>
    /// 等待任一目标状态出现（内部方法）
    /// </summary>
    /// <param name="expectedStates">可接受的目标状态数组</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <returns>转换到的状态，如果超时则返回 default</returns>
    private async Task<TState> EnsureAnyStateTransition(TState[] expectedStates, int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? DefaultTransitionTimeout;
        var sourceState = CurrentState;
        var transitionTargetStates = expectedStates
            .Where(state => !EqualityComparer<TState>.Default.Equals(state, sourceState))
            .ToArray();

        if (transitionTargetStates.Length == 0)
        {
            Logger.LogWarning("状态 {State} 没有可等待的非自身邻接状态", sourceState);
            return default;
        }

        var sourceVisibleStopwatch = Stopwatch.StartNew();
        var intermediateStopwatch = Stopwatch.StartNew();
        var intermediateTimeout = Math.Max(timeout, DefaultIntermediateTransitionTimeout);

        while (sourceVisibleStopwatch.ElapsedMilliseconds < timeout &&
               intermediateStopwatch.ElapsedMilliseconds < intermediateTimeout)
        {
            _ct.ThrowIfCancellationRequested();

            // 只检测期望的状态
            var currentState = DetectStateAmong(transitionTargetStates);
            if (transitionTargetStates.Contains(currentState))
            {
                CurrentState = currentState;
                Logger.LogDebug("状态转换成功：{State}", currentState);
                return currentState;
            }

            var detectedState = DetectCurrentState();
            if (EqualityComparer<TState>.Default.Equals(detectedState, sourceState))
            {
                intermediateStopwatch.Restart();
            }
            else
            {
                sourceVisibleStopwatch.Restart();
            }

            await Delay(DefaultDetectionInterval, _ct);
        }

        if (sourceVisibleStopwatch.ElapsedMilliseconds >= timeout)
        {
            Logger.LogWarning("状态转换超时，期望任一：{Expected}，当前仍停留在原状态：{Current}",
                string.Join(",", transitionTargetStates), sourceState);
        }
        else
        {
            Logger.LogWarning("状态转换超时，期望任一：{Expected}，中间态等待超过 {Timeout} ms，当前：{Current}",
                string.Join(",", transitionTargetStates), intermediateTimeout, DetectCurrentState());
        }
        return default;
    }

    /// <summary>
    /// 等待转换到邻接状态（使用状态转换关系）
    /// 自动使用当前状态注册的邻接状态作为期望状态
    /// </summary>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <returns>转换到的状态，如果没有注册邻接状态则返回 default</returns>
    protected async Task<TState> EnsureNextStateTransition(int? timeoutMs = null)
    {
        var possibleStates = GetPossibleNextStates();
        if (possibleStates == null || possibleStates.Length == 0)
        {
            Logger.LogWarning("当前状态 {State} 没有注册邻接状态，无法确定期望状态", CurrentState);
            return default;
        }

        return await EnsureAnyStateTransition(possibleStates, timeoutMs);
    }

    #endregion
}
