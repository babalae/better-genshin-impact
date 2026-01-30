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
/// 状态处理器委托
/// </summary>
/// <typeparam name="TContext">上下文类型</typeparam>
public delegate Task StateHandlerDelegate<in TContext>(TContext context);

/// <summary>
/// 通用状态机基类
/// 设计理念：
/// 1. 状态检测：通过UI特征判断当前状态
/// 2. 状态转换确认：执行动作后等待状态真正变化
/// 3. 按钮点击间隔：防止因检测延迟导致的连续点击
/// 4. 注册式状态处理：子类只需注册状态处理器，无需编写 switch-case
/// </summary>
/// <typeparam name="TState">状态枚举类型</typeparam>
/// <typeparam name="TContext">上下文类型（如 BvPage）</typeparam>
public abstract class StateMachineBase<TState, TContext> where TState : struct, Enum
{
    /// <summary>
    /// 获取日志记录器 - 由子类实现以提供具体类型的 Logger
    /// </summary>
    protected abstract ILogger Logger { get; }

    protected CancellationToken _ct;

    /// <summary>
    /// 当前状态
    /// </summary>
    public TState CurrentState { get; protected set; }

    /// <summary>
    /// 上次按钮点击时间（防止连点）
    /// </summary>
    private DateTime _lastButtonClickTime = DateTime.MinValue;

    /// <summary>
    /// 状态处理器注册表
    /// </summary>
    private readonly Dictionary<TState, StateHandlerDelegate<TContext>> _stateHandlers = new();

    /// <summary>
    /// 未知状态处理器
    /// </summary>
    private StateHandlerDelegate<TContext>? _unknownStateHandler;

    /// <summary>
    /// 默认按钮点击间隔（毫秒）- 防止连点
    /// </summary>
    protected virtual int DefaultButtonInterval => 500;

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
    /// 注册未知状态处理器
    /// </summary>
    protected void RegisterUnknownStateHandler(StateHandlerDelegate<TContext> handler)
    {
        _unknownStateHandler = handler;
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
        _lastButtonClickTime = DateTime.MinValue;
    }

    /// <summary>
    /// 运行状态机直到达到任一目标状态
    /// 这是核心方法，封装了 switch-case 逻辑
    /// </summary>
    /// <param name="context">上下文对象</param>
    /// <param name="targetStates">目标状态（到达任一即停止）</param>
    /// <param name="maxIterations">最大迭代次数（防止无限循环）</param>
    protected async Task RunStateMachineUntil(TContext context, TState[] targetStates, int maxIterations = 1000)
    {
        Logger.LogInformation("========== 状态机启动，目标状态：{Targets} ==========",
            string.Join(", ", targetStates));

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            _ct.ThrowIfCancellationRequested();

            // 检测当前实际状态
            RefreshCurrentState();

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
                await handler(context);
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

    #region 核心状态机方法 - 闭环设计模式

    /// <summary>
    /// 检测当前UI状态
    /// 子类必须实现此方法，通过UI特征判断当前处于哪个状态
    /// </summary>
    /// <returns>检测到的状态</returns>
    protected abstract TState DetectCurrentState();

    /// <summary>
    /// 更新当前状态（通过检测）
    /// </summary>
    protected void RefreshCurrentState()
    {
        var detected = DetectCurrentState();
        if (!EqualityComparer<TState>.Default.Equals(detected, default))
        {
            CurrentState = detected;
        }
    }

    /// <summary>
    /// 等待状态转换
    /// 执行动作后，等待目标状态出现
    /// </summary>
    /// <param name="expectedState">期望的目标状态</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <returns>是否成功转换到目标状态</returns>
    protected async Task<bool> EnsureStateTransition(TState expectedState, int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? DefaultTransitionTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeout)
        {
            _ct.ThrowIfCancellationRequested();

            var currentState = DetectCurrentState();
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
    /// 等待任一目标状态出现
    /// </summary>
    /// <param name="expectedStates">可接受的目标状态数组</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <returns>转换到的状态，如果超时则返回 default</returns>
    protected async Task<TState> EnsureAnyStateTransition(TState[] expectedStates, int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? DefaultTransitionTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeout)
        {
            _ct.ThrowIfCancellationRequested();

            var currentState = DetectCurrentState();
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
    /// 带间隔的按钮点击
    /// 防止因为检测延迟导致的连续点击
    /// </summary>
    /// <param name="clickAction">点击动作</param>
    /// <param name="interval">点击间隔（毫秒），为null时使用默认值</param>
    protected async Task ClickWithInterval(Action clickAction, int? interval = null)
    {
        var actualInterval = interval ?? DefaultButtonInterval;
        var elapsed = (DateTime.Now - _lastButtonClickTime).TotalMilliseconds;

        if (elapsed < actualInterval)
        {
            await Delay((int)(actualInterval - elapsed), _ct);
        }

        clickAction();
        _lastButtonClickTime = DateTime.Now;
    }

    /// <summary>
    /// 执行动作并确保状态转换（核心闭环模式）
    /// 点击后确认状态已变化，如果没变化则重试
    /// </summary>
    /// <param name="clickAction">点击动作</param>
    /// <param name="expectedState">期望的目标状态</param>
    /// <param name="timeoutMs">总超时时间（毫秒）</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <returns>是否成功转换到目标状态</returns>
    protected async Task<bool> ClickAndEnsure(
        Action clickAction,
        TState expectedState,
        int? timeoutMs = null,
        int? maxRetries = null)
    {
        var timeout = timeoutMs ?? DefaultTransitionTimeout;
        var retries = maxRetries ?? DefaultMaxRetries;

        for (int i = 0; i < retries; i++)
        {
            await ClickWithInterval(clickAction);

            if (await EnsureStateTransition(expectedState, timeout / retries))
            {
                return true;
            }

            Logger.LogDebug("点击后状态未变化，重试 {Retry}/{Max}", i + 1, retries);
        }

        return false;
    }

    /// <summary>
    /// 执行动作并等待任一目标状态出现
    /// </summary>
    /// <param name="clickAction">点击动作</param>
    /// <param name="expectedStates">可接受的目标状态数组</param>
    /// <param name="timeoutMs">总超时时间（毫秒）</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <returns>转换到的状态，如果失败则返回 default</returns>
    protected async Task<TState> ClickAndEnsureAny(
        Action clickAction,
        TState[] expectedStates,
        int? timeoutMs = null,
        int? maxRetries = null)
    {
        var timeout = timeoutMs ?? DefaultTransitionTimeout;
        var retries = maxRetries ?? DefaultMaxRetries;

        for (int i = 0; i < retries; i++)
        {
            await ClickWithInterval(clickAction);

            var result = await EnsureAnyStateTransition(expectedStates, timeout / retries);
            if (!EqualityComparer<TState>.Default.Equals(result, default))
            {
                return result;
            }

            Logger.LogDebug("点击后状态未变化，重试 {Retry}/{Max}", i + 1, retries);
        }

        return default;
    }

    #endregion
}
