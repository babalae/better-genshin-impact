using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    /// 成功：操作完成，状态机自动等待邻接状态转换
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
    protected virtual int DefaultTransitionTimeout => 10000;

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
        _stateTransitions[fromState] = toStates;
    }

    /// <summary>
    /// 批量注册状态转换关系【推荐】
    /// </summary>
    protected void RegisterStateTransitions(params (TState from, TState[] to)[] transitions)
    {
        foreach (var (from, to) in transitions)
        {
            _stateTransitions[from] = to;
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
    /// <param name="maxRetries">单个状态最大重试次数（Retry 返回值触发）</param>
    protected async Task RunStateMachineUntil(TContext context, TState[] targetStates, int maxIterations = 1000, int maxRetries = 5)
    {
        Logger.LogInformation("========== 状态机启动，目标状态：{Targets} ==========",
            string.Join(", ", targetStates));

        int retryCount = 0;
        TState lastRetryState = default;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            _ct.ThrowIfCancellationRequested();

            // 检测当前实际状态
            RefreshCurrentState();

            // 状态变化时重置重试计数
            if (!EqualityComparer<TState>.Default.Equals(CurrentState, lastRetryState))
            {
                retryCount = 0;
                lastRetryState = CurrentState;
            }

            // 检查是否到达目标状态
            if (targetStates.Contains(CurrentState))
            {
                Logger.LogInformation("========== 状态机完成，到达目标状态：{State} ==========", CurrentState);
                return;
            }

            Logger.LogDebug("状态机迭代 {Iteration}，当前状态：{State}", iteration, CurrentState);

            // 执行状态处理器
            if (_stateHandlers.TryGetValue(CurrentState, out var handler))
            {
                var result = await handler(context);

                switch (result)
                {
                    case StateHandlerResult.Success:
                        // 成功：等待邻接状态转换
                        retryCount = 0; // 重置重试计数
                        var nextState = await EnsureNextStateTransition();
                        if (EqualityComparer<TState>.Default.Equals(nextState, default))
                        {
                            Logger.LogWarning("等待邻接状态转换超时");
                        }
                        break;

                    case StateHandlerResult.Wait:
                        // 可预期的等待：继续循环，重新检测状态
                        Logger.LogDebug("Handler 返回 Wait，状态机继续等待");
                        break;

                    case StateHandlerResult.Retry:
                        // 意外失败重试：检查重试次数
                        retryCount++;
                        if (retryCount >= maxRetries)
                        {
                            Logger.LogError("状态 {State} 重试次数达到上限 {Max}，转为失败", CurrentState, maxRetries);
                            throw new InvalidOperationException($"状态 {CurrentState} 重试次数达到上限 {maxRetries}");
                        }
                        Logger.LogWarning("Handler 返回 Retry，将重试当前状态（第 {Count}/{Max} 次）", retryCount, maxRetries);
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

            await Delay(StateMachineLoopInterval, _ct);
        }

        Logger.LogWarning("状态机达到最大迭代次数 {Max}，强制退出", maxIterations);
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
    /// 等待状态转换（内部方法）
    /// 执行动作后，等待目标状态出现
    /// </summary>
    /// <param name="expectedState">期望的目标状态</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <returns>是否成功转换到目标状态</returns>
    private async Task<bool> EnsureStateTransition(TState expectedState, int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? DefaultTransitionTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeout)
        {
            _ct.ThrowIfCancellationRequested();

            // 只检测期望的状态
            var currentState = DetectStateAmong([expectedState]);
            if (EqualityComparer<TState>.Default.Equals(currentState, expectedState))
            {
                CurrentState = currentState;
                Logger.LogDebug("状态转换成功：{State}", expectedState);
                return true;
            }

            await Delay(DefaultDetectionInterval, _ct);
        }

        Logger.LogWarning("状态转换超时，期望：{Expected}，当前：{Current}", expectedState, DetectCurrentState());
        return false;
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
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeout)
        {
            _ct.ThrowIfCancellationRequested();

            // 只检测期望的状态
            var currentState = DetectStateAmong(expectedStates);
            if (expectedStates.Contains(currentState))
            {
                CurrentState = currentState;
                Logger.LogDebug("状态转换成功：{State}", currentState);
                return currentState;
            }

            await Delay(DefaultDetectionInterval, _ct);
        }

        Logger.LogWarning("状态转换超时，期望任一：{Expected}，当前：{Current}",
            string.Join(",", expectedStates), DetectCurrentState());
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
