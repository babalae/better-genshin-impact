using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Input.Backends.Win32;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 游戏运行环境的编排：选定 Provider，负责启动（绑定）、停止（解绑），并持有当前运行环境。
/// <para>
/// 所有状态变更都在 UI 线程上串行执行：窗口与 WinEventHook 依赖消息循环，
/// 而 StartAsync / Stop 可能从任务线程或截图调度线程调用。
/// </para>
/// </summary>
public sealed class GameRuntimeService
{
    private readonly IGameRuntimeProvider _provider;
    private readonly TaskTriggerDispatcher _dispatcher;
    private readonly IConfigService _configService;
    private readonly ILogger<GameRuntimeService> _logger;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private CancellationTokenSource? _startCts;

    public GameRuntimeService(
        IEnumerable<IGameRuntimeProvider> providers,
        InstanceBootstrap bootstrap,
        TaskTriggerDispatcher dispatcher,
        IConfigService configService,
        ILogger<GameRuntimeService> logger)
    {
        _dispatcher = dispatcher;
        _configService = configService;
        _logger = logger;

        var registered = providers.ToList();
        var expectedKind = bootstrap.Context.InstanceType == BetterGiInstanceType.WebView
            ? GameRuntimeKind.WebPage
            : GameRuntimeKind.Win32Window;
        var provider = registered.FirstOrDefault(p => p.Kind == expectedKind);
        if (provider is null)
        {
            // TODO(P2)：注册 WebPageRuntimeProvider 后删除这个回退
            provider = registered.First(p => p.Kind == GameRuntimeKind.Win32Window);
            _logger.LogWarning("未注册 {Expected} 运行环境，暂按 {Actual} 处理", expectedKind, provider.Kind);
        }

        _provider = provider;
        _dispatcher.UiTaskStopTickEvent += OnRuntimeLost;
    }

    /// <summary>
    /// 由实例类型决定，进程内不变
    /// </summary>
    public GameRuntimeKind Kind => _provider.Kind;

    /// <summary>
    /// 当前运行环境。截图器未启动时为 null
    /// </summary>
    public GameRuntime? Current { get; private set; }

    public bool IsRunning => Current is not null;

    /// <summary>
    /// 绑定完成后在 UI 线程上触发
    /// </summary>
    public event EventHandler? Started;

    /// <summary>
    /// 解绑完成后在 UI 线程上触发
    /// </summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// 获取运行环境并启动截图器。已在运行时直接返回 true；等待期间可以被 <see cref="Stop"/> 取消。
    /// 绑定过程中抛出的异常（例如分辨率不合规）会在回滚后继续向上抛出，与改造前一致
    /// </summary>
    public Task<bool> StartAsync(CancellationToken ct = default)
    {
        var uiDispatcher = Application.Current.Dispatcher;
        return uiDispatcher.CheckAccess()
            ? StartCoreAsync(ct)
            : uiDispatcher.InvokeAsync(() => StartCoreAsync(ct)).Task.Unwrap();
    }

    /// <summary>
    /// 用外部构造好的运行环境启动截图器，目前只用于手动选窗。在 UI 线程上调用。
    /// 无法启动时释放传入的运行环境并返回 false
    /// </summary>
    public bool Start(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Application.Current.Dispatcher.VerifyAccess();

        if (IsRunning || _startLock.CurrentCount == 0 || !CheckTriggerInterval())
        {
            runtime.Input.Dispose();
            runtime.Dispose();
            return false;
        }

        Bind(runtime);
        return true;
    }

    /// <summary>
    /// 停止截图器：取消任务、停止调度、释放运行环境。不关闭游戏
    /// </summary>
    public void Stop()
    {
        var uiDispatcher = Application.Current.Dispatcher;
        if (!uiDispatcher.CheckAccess())
        {
            uiDispatcher.Invoke(Stop);
            return;
        }

        // 取消进行中的启动（例如等待游戏就绪）
        _startCts?.Cancel();

        var runtime = Current;
        if (runtime is null)
        {
            return;
        }

        Current = null;
        CancellationContext.Instance.Cancel(); // 取消独立任务的运行
        _dispatcher.Stop();
        // 旧后端先 ReleaseAll 再释放
        InputHub.Attach(new Win32InputBackend(IntPtr.Zero));
        TaskContext.Instance().Bind(null);
        runtime.Dispose();
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 关闭游戏。截图器会在下一次调度时发现游戏已退出并自动停止
    /// </summary>
    public void CloseGame() => _provider.CloseGame();

    private async Task<bool> StartCoreAsync(CancellationToken ct)
    {
        await _startLock.WaitAsync(ct);
        try
        {
            if (IsRunning)
            {
                return true;
            }

            if (!CheckTriggerInterval())
            {
                return false;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _startCts = cts;
            GameRuntime? runtime;
            try
            {
                runtime = await _provider.AcquireAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                _logger.LogInformation("已取消启动截图器");
                return false;
            }
            finally
            {
                _startCts = null;
            }

            if (runtime is null)
            {
                return false;
            }

            Bind(runtime);
            return true;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private void Bind(GameRuntime runtime)
    {
        var inputAttached = false;
        try
        {
            TaskContext.Instance().Bind(runtime);
            InputHub.Attach(runtime.Input);
            inputAttached = true;
            _dispatcher.Start(runtime, _configService.Get().TriggerInterval);
        }
        catch
        {
            _dispatcher.Stop();
            if (inputAttached)
            {
                InputHub.Attach(new Win32InputBackend(IntPtr.Zero));
            }
            else
            {
                runtime.Input.Dispose();
            }

            TaskContext.Instance().Bind(null);
            runtime.Dispose();
            throw;
        }

        Current = runtime;
        _logger.LogDebug("游戏运行环境已绑定：{Kind}，窗口句柄 {Handle}", runtime.Kind, runtime.Window.Handle);
        Started?.Invoke(this, EventArgs.Empty);
    }

    private bool CheckTriggerInterval()
    {
        if (_configService.Get().TriggerInterval > 0)
        {
            return true;
        }

        ThemedMessageBox.Error("触发器触发频率必须大于0");
        return false;
    }

    /// <summary>
    /// 截图调度发现游戏已退出或截图器停止
    /// </summary>
    private void OnRuntimeLost(object? sender, EventArgs e) => Stop();
}
