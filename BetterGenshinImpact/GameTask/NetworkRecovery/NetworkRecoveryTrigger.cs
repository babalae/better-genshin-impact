using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.NetworkRecovery;

/// <summary>
/// 网络健康实时触发器。Ping 循环独立于截图回调；截图回调只负责在网络恢复后
/// 启动一次恢复流程，因此挂起任务线程不会切断自身的恢复路径。
/// </summary>
public sealed class NetworkRecoveryTrigger : ITaskTrigger
{
    private static readonly object Sync = new();
    private static readonly ILogger Logger = App.GetLogger<NetworkRecoveryTrigger>();
    private static NetworkRecoveryController? _controller;
    private static IDisposable? _controllerScope;
    private static bool _sessionActive;
    private static long _lastRecoveryCheckTicks;

    public string Name => "NetworkRecovery";

    public bool IsEnabled
    {
        get => TaskContext.Instance().Config.OtherConfig.NetworkHealthMonitoringEnabled;
        set { }
    }

    public int Priority => 5;
    public bool IsExclusive => false;
    // 用户切到 BGI 或系统网络面板恢复网络时也必须继续执行，否则无法主动切回并恢复游戏。
    public bool IsBackgroundRunning => true;
    public bool AlwaysActive => true;

    public static void OnCaptureSessionStarted()
    {
        lock (Sync) _sessionActive = true;
    }

    public void Init()
    {
        if (IsEnabled) _ = EnsureController();
        else StopController();
    }

    public void OnCapture(CaptureContent content)
    {
        if (!IsEnabled)
        {
            StopController();
            return;
        }

        var controller = EnsureController();
        if (controller is null) return;
        if (!controller.IsPaused) return;

        // 有任务持有信号量时，必须等任务线程真正走到暂停检查点；否则恢复流程会与原任务并发操作游戏。
        // 空闲时则由恢复流程临时持有任务信号量，阻止恢复期间启动新任务。
        var ownsTaskSemaphore = false;
        if (!controller.HasTaskPauseWaiter)
        {
            if (!TaskControl.TaskSemaphore.Wait(0)) return;
            ownsTaskSemaphore = true;
        }

        // 截图频率很高；恢复检查最多每秒一次，真正恢复仍由控制器单飞。
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref _lastRecoveryCheckTicks);
        if (now - previous < TimeSpan.FromSeconds(1).Ticks ||
            Interlocked.CompareExchange(ref _lastRecoveryCheckTicks, now, previous) != previous)
        {
            if (ownsTaskSemaphore) TaskControl.TaskSemaphore.Release();
            return;
        }

        _ = Task.Run(() => TryRecoverAsync(controller, ownsTaskSemaphore));
    }

    private static NetworkRecoveryController? EnsureController()
    {
        lock (Sync)
        {
            if (!_sessionActive) return null;

            if (_controller is not null) return _controller;
            _controller = NetworkRecoveryTask.CreateController(CancellationToken.None);
            _controllerScope = _controller.Enter();
            Interlocked.Exchange(ref _lastRecoveryCheckTicks, 0);
            return _controller;
        }
    }

    private static async Task TryRecoverAsync(NetworkRecoveryController controller, bool ownsTaskSemaphore)
    {
        try
        {
            await controller.TryRecoverAsync(controller.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (controller.Token.IsCancellationRequested)
        {
            // 截图会话结束时的正常收尾。
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "网络恢复触发器执行失败，等待下次重试");
        }
        finally
        {
            if (ownsTaskSemaphore) TaskControl.TaskSemaphore.Release();
        }
    }

    public static void StopSession()
    {
        lock (Sync) _sessionActive = false;
        StopController();
    }

    private static void StopController()
    {
        NetworkRecoveryController? controller;
        IDisposable? scope;
        lock (Sync)
        {
            controller = _controller;
            scope = _controllerScope;
            _controller = null;
            _controllerScope = null;
        }

        if (controller is null) return;
        try { controller.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception e) { Logger.LogWarning(e, "停止网络健康实时触发器时发生异常"); }
        finally { scope?.Dispose(); }
    }
}
