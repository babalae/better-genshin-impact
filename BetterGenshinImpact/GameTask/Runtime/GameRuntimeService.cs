using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Input.Backends.Win32;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.Runtime.WebPage;
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
/// 而 StartAsync / StopAsync 可能从任务线程或截图调度线程调用。
/// </para>
/// </summary>
public sealed class GameRuntimeService
{
    private readonly IGameRuntimeProvider _provider;
    private readonly TaskTriggerDispatcher _dispatcher;
    private readonly IConfigService _configService;
    private readonly ILogger<GameRuntimeService> _logger;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Task _stopTask = Task.CompletedTask;
    private long _stopVersion;

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
        _provider = registered.FirstOrDefault(p => p.Kind == expectedKind)
                    ?? throw new InvalidOperationException($"未注册 {expectedKind} 运行环境");
        if (_provider is WebPageRuntimeProvider webPageProvider)
        {
            webPageProvider.StopCaptureBeforeCloseAsync = StopAsync;
        }

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
    /// 正在获取运行环境（找窗、启动游戏、等待网页版登录与排队）。只在 UI 线程上变化
    /// </summary>
    public bool IsStarting { get; private set; }

    /// <summary>
    /// <see cref="IsStarting"/> 变化后在 UI 线程上触发
    /// </summary>
    public event EventHandler? StartingChanged;

    /// <summary>
    /// 绑定完成后在 UI 线程上触发
    /// </summary>
    public event EventHandler? Started;

    /// <summary>
    /// 解绑完成后在 UI 线程上触发
    /// </summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// 获取运行环境并启动截图器。已在运行时直接返回 true；停止请求会等待本次启动完成后再解绑，
    /// 获取期间收到停止请求时，获取完成后不再绑定（网页版可关闭宿主窗口来结束等待）。
    /// 绑定过程中抛出的异常（例如分辨率不合规）会在回滚后继续向上抛出，与改造前一致
    /// </summary>
    public Task<bool> StartAsync(CancellationToken ct = default)
    {
        var stopVersion = Interlocked.Read(ref _stopVersion);
        var uiDispatcher = Application.Current.Dispatcher;
        return uiDispatcher.CheckAccess()
            ? StartCoreAsync(ct, stopVersion)
            : uiDispatcher.InvokeAsync(() => StartCoreAsync(ct, stopVersion)).Task.Unwrap();
    }

    /// <summary>
    /// 用外部构造好的运行环境启动截图器，目前只用于手动选窗。在 UI 线程上调用。
    /// 无法启动时释放传入的运行环境并返回 false
    /// </summary>
    public bool Start(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Application.Current.Dispatcher.VerifyAccess();

        if (IsRunning || !_stopTask.IsCompleted || _startLock.CurrentCount == 0 || !CheckTriggerInterval())
        {
            runtime.Input.Dispose();
            runtime.Dispose();
            return false;
        }

        Bind(runtime);
        return true;
    }

    /// <summary>
    /// 等待启动完成后停止截图器：取消任务、停止调度、释放运行环境。不关闭游戏
    /// </summary>
    public Task StopAsync()
    {
        Interlocked.Increment(ref _stopVersion);
        var uiDispatcher = Application.Current.Dispatcher;
        return uiDispatcher.CheckAccess()
            ? QueueStopAsync()
            : uiDispatcher.InvokeAsync(QueueStopAsync).Task.Unwrap();
    }

    private Task QueueStopAsync()
    {
        // 同一轮停止只排队一次；后续启动必须等这次解绑完成。
        if (_stopTask.IsCompleted)
        {
            _stopTask = StopCoreAsync();
        }

        return _stopTask;
    }

    private async Task StopCoreAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            StopCurrent();
        }
        finally
        {
            _startLock.Release();
        }
    }

    private void StopCurrent()
    {
        var runtime = Current;
        if (runtime is null)
        {
            return;
        }

        Current = null;
        CancellationContext.Instance.Cancel(); // 取消独立任务的运行
        _dispatcher.Stop();
        ReleaseInputBeforeDetach();
        InputHub.Attach(new Win32InputBackend(IntPtr.Zero));
        TaskContext.Instance().Bind(null);
        runtime.Dispose();
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 关闭游戏。截图器会在下一次调度时发现游戏已退出并自动停止
    /// </summary>
    public void CloseGame() => _provider.CloseGame();

    private async Task<bool> StartCoreAsync(CancellationToken ct, long stopVersion)
    {
        await _stopTask.WaitAsync(ct);
        await _startLock.WaitAsync(ct);
        try
        {
            // 停止请求之前排队的启动不能在解绑后重新启动截图器。
            if (stopVersion != Interlocked.Read(ref _stopVersion))
            {
                return false;
            }

            if (IsRunning)
            {
                return true;
            }

            if (!CheckTriggerInterval())
            {
                return false;
            }

            GameRuntime? runtime;
            SetStarting(true);
            try
            {
                runtime = await _provider.AcquireAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("已取消启动截图器");
                return false;
            }
            finally
            {
                SetStarting(false);
            }

            if (runtime is null)
            {
                return false;
            }

            // 获取期间收到了停止请求：不再绑定，等价于"绑定后立即解绑"，但不会闪一下遮罩、也不会加载触发器
            if (stopVersion != Interlocked.Read(ref _stopVersion))
            {
                runtime.Input.Dispose();
                runtime.Dispose();
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
                ReleaseInputBeforeDetach();
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

    private void SetStarting(bool value)
    {
        if (IsStarting == value)
        {
            return;
        }

        IsStarting = value;
        StartingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseInputBeforeDetach()
    {
        try
        {
            InputHub.ReleaseAll();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "解绑运行环境时释放按键失败");
        }
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
    private async void OnRuntimeLost(object? sender, EventArgs e)
    {
        try
        {
            await StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "游戏运行环境停止失败");
        }
    }
}
