using BetterGenshinImpact.Core.Profile;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.View.Drawable;
using Fischless.GameCapture;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.Session;

/// <summary>
/// 一次独立游戏运行实例的任务作用域。
/// 会话聚合目标、截图、输入、调度器及所有可变任务上下文，使多个云实例可以并发运行；
/// PC 链路未进入此作用域时仍使用原有全局单例。
/// </summary>
public sealed class GameSession : IAsyncDisposable
{
    // 会话基础设施的生命周期取消源，健康检查等后台任务监听该令牌。
    private readonly CancellationTokenSource _lifetimeCts = new();

    // 当前实例卡片展示的有界日志队列。
    private readonly ConcurrentQueue<string> _logEntries = new();

    /// <summary>
    /// 创建并装配一个独立的任务会话上下文。
    /// </summary>
    /// <param name="profile">会话对应的通用 Profile。</param>
    /// <param name="target">游戏运行目标。</param>
    /// <param name="input">会话独占输入后端。</param>
    /// <param name="capture">会话独占截图源。</param>
    /// <param name="systemInfo">固定画面和 DesktopRegion 信息。</param>
    public GameSession(
        BetterGiProfile profile,
        IGameTarget target,
        IGameInputBackend input,
        IGameCapture capture,
        ISystemInfo systemInfo)
    {
        // Profile Id 同时作为稳定 SessionId，便于目录、UI 和日志关联。
        Profile = profile;
        Id = profile.Id;
        Target = target;
        Input = input;
        Capture = capture;
        // 以下对象都是原静态单例的会话级替代品，不允许跨会话共享可变状态。
        TaskContext = TaskContext.CreateSessionContext(target.WindowHandle, systemInfo);
        CancellationContext = new CancellationContext();
        RunnerContext = new RunnerContext();
        VisionContext = new VisionContext();
        TaskSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// 获取稳定会话标识。
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 获取当前会话对应的 Profile。
    /// </summary>
    public BetterGiProfile Profile { get; }

    /// <summary>
    /// 获取当前会话生命周期状态。
    /// </summary>
    public GameSessionState State { get; internal set; } = GameSessionState.Starting;

    /// <summary>
    /// 获取实例卡片显示的状态文本。
    /// </summary>
    public string StatusMessage { get; internal set; } = "正在启动";

    /// <summary>
    /// 获取当前会话独占的运行目标。
    /// </summary>
    public IGameTarget Target { get; }

    /// <summary>
    /// 获取当前会话独占的输入后端。
    /// </summary>
    public IGameInputBackend Input { get; }

    /// <summary>
    /// 获取当前会话独占的截图源。
    /// </summary>
    public IGameCapture Capture { get; }

    /// <summary>
    /// 获取当前会话的任务上下文。
    /// </summary>
    public TaskContext TaskContext { get; }

    /// <summary>
    /// 获取当前会话的任务取消上下文。
    /// </summary>
    public CancellationContext CancellationContext { get; }

    /// <summary>
    /// 获取当前会话的脚本运行上下文。
    /// </summary>
    public RunnerContext RunnerContext { get; }

    /// <summary>
    /// 获取当前会话的识图绘制上下文。
    /// </summary>
    public VisionContext VisionContext { get; }

    /// <summary>
    /// 获取当前会话独立的任务互斥信号量。
    /// </summary>
    public SemaphoreSlim TaskSemaphore { get; }

    /// <summary>
    /// 获取当前会话独占的实时触发器调度器。
    /// </summary>
    public TaskTriggerDispatcher? Dispatcher { get; private set; }

    // 当前会话已经创建的触发器实例，替代 GameTaskManager 的 PC 全局字典。
    internal ConcurrentDictionary<string, ITaskTrigger>? TriggerDictionary { get; set; }

    // 当前会话脚本组项目的执行次数统计。
    internal Dictionary<string, int> ScriptProjectExecutionCount { get; } = new();

    // 当前会话“自动开门”触发器开关。
    internal bool GameLoadingEnabled { get; set; }

    // 当前会话聊天界面热键保护状态。
    internal ChatUiHotkeyGuardState ChatUiHotkeyGuardState { get; } = new();

    /// <summary>
    /// 获取会话基础设施生命周期令牌。
    /// </summary>
    public CancellationToken LifetimeToken => _lifetimeCts.Token;

    /// <summary>
    /// 会话状态或状态消息改变时触发。
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// 当前会话新增一条带 SessionId 的日志时触发。
    /// </summary>
    public event EventHandler<string>? LogAdded;

    /// <summary>
    /// 获取当前最多一百条会话日志的快照。
    /// </summary>
    public IReadOnlyCollection<string> LogEntries => _logEntries.ToArray();

    /// <summary>
    /// 更新会话状态并写入带 SessionId 的实例日志。
    /// 相同状态和消息不会重复通知，避免健康检查持续刷屏。
    /// </summary>
    internal void UpdateState(GameSessionState state, string message)
    {
        if (State == state && StatusMessage == message)
        {
            // 健康检查每秒运行，完全相同的状态无需重复刷新 UI。
            return;
        }

        // 先更新可读取状态，再通知日志和 UI 订阅方。
        State = state;
        StatusMessage = message;
        AddLog(message);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 写入一条带时间和 SessionId 的有界实例日志。
    /// </summary>
    internal void AddLog(string message)
    {
        // SessionId 保留完整 Guid，便于与全局日志精确关联。
        var entry = $"[{DateTime.Now:HH:mm:ss}] [SessionId={Id}] {message}";
        _logEntries.Enqueue(entry);
        while (_logEntries.Count > 100)
        {
            // 卡片只保留最近一百条，避免长期运行导致集合无限增长。
            _logEntries.TryDequeue(out _);
        }
        LogAdded?.Invoke(this, entry);
    }

    /// <summary>
    /// 将创建完成的会话调度器挂接到当前会话。
    /// </summary>
    internal void AttachDispatcher(TaskTriggerDispatcher dispatcher)
    {
        Dispatcher = dispatcher;
    }

    /// <summary>
    /// 启动当前会话专属的实时触发器调度器。
    /// </summary>
    public void StartDispatcher(int interval)
    {
        if (Dispatcher == null)
        {
            throw new InvalidOperationException("云会话调度器尚未初始化");
        }

        // 静态兼容代理必须在会话作用域内解析到当前云实例。
        using var scope = GameSessionContext.Enter(this);
        Dispatcher.StartCloud(Capture, interval);
        AddLog("实时触发器已启动");
    }

    /// <summary>
    /// 在当前会话作用域内停止实时触发器调度器。
    /// </summary>
    public void StopDispatcher()
    {
        // Stop 内部仍会访问会话级聊天保护和截图源。
        using var scope = GameSessionContext.Enter(this);
        Dispatcher?.Stop();
        AddLog("实时触发器已停止");
    }

    /// <summary>
    /// 取消会话、停止截图和调度器、释放输入状态并关闭运行目标。
    /// 清理过程始终处于本会话作用域内，避免误操作 PC 单例。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 释放过程涉及多个静态兼容代理，因此整个流程保持会话作用域。
        using var scope = GameSessionContext.Enter(this);
        _lifetimeCts.Cancel();
        CancellationContext.Cancel();

        // 先停止生产新截图和输入的任务，再释放输入及窗口资源。
        Dispatcher?.Stop();
        Capture.Stop();
        try
        {
            await Input.ReleaseAllAsync();
        }
        catch
        {
            // 页面可能已断开，关闭会话时继续清理其他资源。
        }
        // 输入队列必须先于 WebView2 释放，否则消费者可能访问已经关闭的页面。
        await Input.DisposeAsync();
        await Target.DisposeAsync();
        TaskSemaphore.Dispose();
        _lifetimeCts.Dispose();
    }
}
