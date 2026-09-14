using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Common;

public class TaskControl
{
    public static ILogger Logger { get; } = App.GetLogger<TaskControl>();

    public static readonly SemaphoreSlim TaskSemaphore = new(1, 1);
    private static readonly object PauseSync = new();
    private static readonly object PauseTransitionSync = new();
    private static int _pauseWaiters;
    private static bool _pauseComponentsSuspended;
    private static bool _pauseSideEffectsApplied;
    private static bool _inputReleaseFailureLogged;
    private static long _pauseStartedTimestamp;
    private static long _totalPausedTimestamp;


    public static void CheckAndSleep(int millisecondsTimeout)
    {
        TrySuspend();
        CheckAndActivateGameWindow();

        Thread.Sleep(millisecondsTimeout);
        TrySuspend();
    }

    public static void Sleep(int millisecondsTimeout)
    {
        NewRetry.Do(() =>
        {
            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
        TrySuspend();
    }

    public static void TrySuspend(CancellationToken cancellationToken = default)
    {
        var network = NetworkRecoveryController.Current;
        var effectiveToken = GetEffectiveCancellationToken(cancellationToken);
        var registered = false;
        IDisposable? networkPauseLease = null;
        try
        {
            while (IsPauseRequested(network))
            {
                effectiveToken.ThrowIfCancellationRequested();
                if (!registered)
                {
                    lock (PauseSync) _pauseWaiters++;
                    registered = true;
                }
                if (!ApplyPauseSideEffects())
                {
                    if (!IsPauseRequested(network)) break;
                    if (effectiveToken.WaitHandle.WaitOne(250))
                        effectiveToken.ThrowIfCancellationRequested();
                    continue;
                }

                if (networkPauseLease is null &&
                    network is { IsPaused: true } && !network.IsRecoveryExecution &&
                    !TaskTriggerDispatcher.IsInTriggerCallback)
                {
                    networkPauseLease = network.AcknowledgeTaskPaused();
                }

                if (!IsPauseRequested(network)) break;
                if (effectiveToken.WaitHandle.WaitOne(250))
                    effectiveToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            networkPauseLease?.Dispose();
            if (registered)
            {
                lock (PauseSync)
                {
                    if (_pauseWaiters > 0) _pauseWaiters--;
                    if (_pauseWaiters == 0 &&
                        (!IsPauseRequested(network) || effectiveToken.IsCancellationRequested))
                        ReleasePauseSideEffects();
                }
            }
        }
    }

    private static CancellationToken GetEffectiveCancellationToken(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled || TaskSemaphore.CurrentCount != 0)
            return cancellationToken;

        try { return CancellationContext.Instance.Cts.Token; }
        catch (ObjectDisposedException) { return CancellationToken.None; }
    }

    private static bool IsPauseRequested(NetworkRecoveryController? network) =>
        RunnerContext.Instance.IsSuspend ||
        (network is { IsPaused: true } && !network.IsRecoveryExecution &&
         !TaskTriggerDispatcher.IsInTriggerCallback);

    private static bool ApplyPauseSideEffects()
    {
        // PauseSync 还承担活动时间读取，不能在其中执行窗口切换和固定等待。
        // 单独串行化副作用，确保其他任务分支只能在首个分支完成输入释放后确认暂停。
        lock (PauseTransitionSync)
        {
            var shouldSuspendComponents = false;
            lock (PauseSync)
            {
                if (_pauseSideEffectsApplied) return true;
                if (_pauseStartedTimestamp == 0)
                    _pauseStartedTimestamp = Stopwatch.GetTimestamp();
                if (!_pauseComponentsSuspended)
                {
                    _pauseComponentsSuspended = true;
                    shouldSuspendComponents = true;
                }
            }

            // 即使 Windows 暂时拒绝前台切换，也要先阻止自动拾取、路径等独立分支继续输入。
            if (shouldSuspendComponents)
            {
                RunnerContext.Instance.StopAutoPick();
                foreach (var suspendable in RunnerContext.Instance.SuspendableDictionary.Values.ToArray())
                    suspendable.Suspend();
                Logger.LogWarning(RunnerContext.Instance.IsSuspend
                    ? "快捷键触发暂停，等待解除"
                    : "网络探测失败，任务暂停等待恢复");
            }

            if (!ReleaseAllInputForPause())
            {
                var shouldLog = false;
                lock (PauseSync)
                {
                    if (!_inputReleaseFailureLogged)
                    {
                        _inputReleaseFailureLogged = true;
                        shouldLog = true;
                    }
                }
                if (shouldLog)
                    Logger.LogWarning("暂停时未能激活原神窗口，尚未释放输入，将继续重试");
                return false;
            }

            lock (PauseSync) _pauseSideEffectsApplied = true;
            return true;
        }
    }

    private static bool ReleaseAllInputForPause()
    {
        var taskContext = TaskContext.Instance();
        var gameHandle = taskContext.IsInitialized ? taskContext.GameHandle : IntPtr.Zero;
        var previousForeground = User32.GetForegroundWindow();
        var restoreForeground = gameHandle != IntPtr.Zero && previousForeground != gameHandle &&
                                User32.IsWindow(previousForeground);

        try
        {
            // 上游的手动暂停依赖 SendInput 释放按键；断网暂停时原神不一定在前台，
            // 必须先让真实 KeyUp/MouseUp 到达绑定的游戏窗口。
            if (gameHandle != IntPtr.Zero && previousForeground != gameHandle)
            {
                // FocusWindow 对无法还原的最小化窗口会无限等待；这里使用上游已有的
                // 无循环恢复方法，并保留缓冲时间让 KeyUp/MouseUp 到达游戏窗口。
                SystemControl.RestoreWindow(gameHandle);
                Thread.Sleep(100);
            }

            // RestoreWindow 只发起激活请求；必须确认目标确实成为前台窗口，
            // 否则 SendInput 的 KeyUp/MouseUp 会落到 BGI 或 Explorer。
            if (gameHandle != IntPtr.Zero && User32.GetForegroundWindow() != gameHandle)
                return false;

            Simulation.ReleaseAllKey();
            Thread.Sleep(50);
            return true;
        }
        finally
        {
            if (restoreForeground)
                SystemControl.RestoreWindow((nint)previousForeground);
        }
    }

    private static void ReleasePauseSideEffects()
    {
        if (_pauseStartedTimestamp == 0) return;
        _totalPausedTimestamp += Stopwatch.GetTimestamp() - _pauseStartedTimestamp;
        _pauseStartedTimestamp = 0;
        _inputReleaseFailureLogged = false;
        var componentsSuspended = _pauseComponentsSuspended;
        _pauseComponentsSuspended = false;
        _pauseSideEffectsApplied = false;
        if (!componentsSuspended) return;
        RunnerContext.Instance.ResumeAutoPick();
        foreach (var suspendable in RunnerContext.Instance.SuspendableDictionary.Values.ToArray())
            suspendable.Resume();
        Logger.LogWarning("暂停已经解除");
    }

    /// <summary>任务收尾兜底，避免取消发生在暂停循环时遗留自动拾取计数。</summary>
    public static void ResetPauseSideEffects()
    {
        // 避免任务收尾在首个暂停分支尚未完成副作用时先行恢复。
        lock (PauseTransitionSync)
        {
            lock (PauseSync)
            {
                _pauseWaiters = 0;
                ReleasePauseSideEffects();
            }
        }
    }

    /// <summary>返回扣除任务暂停时长后的单调时间戳，供任务超时使用。</summary>
    public static long GetActiveTimestamp()
    {
        lock (PauseSync)
        {
            var now = Stopwatch.GetTimestamp();
            var paused = _totalPausedTimestamp;
            if (_pauseStartedTimestamp != 0)
                paused += now - _pauseStartedTimestamp;
            return now - paused;
        }
    }

    /// <summary>计算从指定活动时间戳起、扣除暂停时长后的经过时间。</summary>
    public static TimeSpan GetActiveElapsed(long startedAt) =>
        Stopwatch.GetElapsedTime(startedAt, GetActiveTimestamp());

    private static void CheckAndActivateGameWindow()
    {
        // 恢复流程需要向原神发送真实键鼠输入，不能受普通的“失焦后恢复”开关限制。
        // 否则恢复期间偶发失焦会让后续确认/登录操作停在其他窗口上。
        var shouldRestoreFocus = TaskContext.Instance().Config.OtherConfig.RestoreFocusOnLostEnabled ||
                                 NetworkRecoveryController.Current is { IsRecoveryExecution: true };
        if (!shouldRestoreFocus)
        {
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogWarning($"当前获取焦点的窗口为: {name}，不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }

        var count = 0;
        //未激活则尝试恢复窗口
        while (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            if (count >= 10 && count % 10 == 0)
            {
                Logger.LogInformation("多次尝试未恢复，尝试最小化后激活窗口！");
                SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
            }
            else
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogInformation("当前获取焦点的窗口为: {Name}，不是原神，尝试恢复窗口", name);
                SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
            }

            count++;
            Thread.Sleep(1000);
        }
    }

    public static void Sleep(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(ct);
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        // 暂停可能在 Thread.Sleep 期间到达；不要让调用方在返回后继续输入。
        TrySuspend(ct);
    }

    public static async Task Delay(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct is { IsCancellationRequested: true })
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(ct);
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        await Task.Delay(millisecondsTimeout, ct);
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }

        // 暂停可能在 Task.Delay 期间到达；返回调用方前再次进入暂停检查点。
        TrySuspend(ct);

        // NewRetry 等上游流程通常在 Delay 返回后立即发送下一次输入。
        // 网络恢复期间需要在等待结束时再校验一次，堵住“等待中失焦、返回后误点其他窗口”的竞态。
        if (NetworkRecoveryController.Current is { IsRecoveryExecution: true })
        {
            CheckAndActivateGameWindow();
        }
    }

    /// <summary>
    /// 模拟长按指定动作。使用 try/finally 块确保在任务被取消或发生异常时，按键也能安全释放，防止卡键。
    /// </summary>
    /// <param name="action">需要模拟的游戏动作（如元素战技、普通攻击等）</param>
    /// <param name="holdMs">长按持续的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    public static async Task SimulateHoldActionAsync(GIActions action, int holdMs, CancellationToken ct)
    {
        try
        {
            Simulation.SendInput.SimulateAction(action, KeyType.KeyDown);
            await Delay(holdMs, ct);
        }
        finally
        {
            Simulation.SendInput.SimulateAction(action, KeyType.KeyUp);        
        }
    }

    /// <summary>
    /// 模拟长按元素战技（如万叶长E）。包含释放前摇、长按以及释放后的缓冲延时。
    /// </summary>
    /// <param name="holdMs">元素战技按住的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="releaseLeftMouseBefore">是否在按下元素战技前先松开鼠标左键，避免输入冲突，默认 true</param>
    /// <param name="releaseLeftMouseDelayMs">松开鼠标左键后的缓冲时间（毫秒），默认 10ms</param>
    /// <param name="postKeyUpDelayMs">元素战技释放后的缓冲时间（毫秒），默认 50ms</param>
    public static async Task SimulateHoldElementalSkillAsync(
        int holdMs,
        CancellationToken ct,
        bool releaseLeftMouseBefore = true,
        int releaseLeftMouseDelayMs = 10,
        int postKeyUpDelayMs = 50)
    {
        if (releaseLeftMouseBefore)
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
            await Delay(releaseLeftMouseDelayMs, ct);
        }

        await SimulateHoldActionAsync(GIActions.ElementalSkill, holdMs, ct);   
        await Delay(postKeyUpDelayMs, ct);
    }

    /// <summary>
    /// 模拟鼠标左键连续点击循环（如万叶长E后的下落攻击）。双层 try/finally 设计以确保无论在循环的哪个阶段发生取消或异常，鼠标左键都会被强制释放。
    /// </summary>
    /// <param name="repeatCount">需要循环点击的次数</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="preUpDelayMs">每次点击前，预先抬起左键后的缓冲延时（毫秒），默认 10ms</param>
    /// <param name="downHoldMs">鼠标左键按下的保持时间（毫秒），默认 35ms</param>
    /// <param name="postUpDelayMs">每次点击完成后的等待时间（毫秒），默认 50ms</param>
    public static async Task SimulateMouseLeftClickLoopAsync(
        int repeatCount,
        CancellationToken ct,
        int preUpDelayMs = 10,
        int downHoldMs = 35,
        int postUpDelayMs = 50)
    {
        try
        {
            for (var i = 0; i < repeatCount; i++)
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
                await Delay(preUpDelayMs, ct);
                Simulation.SendInput.Mouse.LeftButtonDown();
                try
                {
                    await Delay(downHoldMs, ct);
                }
                finally
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                }

                await Delay(postUpDelayMs, ct);
            }
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }
    }

    public static Mat CaptureGameImage(IGameCapture? gameCapture)
    {
        var captureFrame = gameCapture?.Capture();
        var image = captureFrame?.Frame;
        if (image == null)
        {
            captureFrame?.Dispose();
            Logger.LogWarning("截图失败!");
            // 重试3次
            for (var i = 0; i < 3; i++)
            {
                captureFrame = gameCapture?.Capture();
                image = captureFrame?.Frame;
                if (image != null)
                {
                    return image;
                }

                captureFrame?.Dispose();
                Sleep(30);
            }

            throw new Exception("尝试多次后,截图失败!");
        }
        else
        {
            return image;
        }
    }

    public static Mat? CaptureGameImageNoRetry(IGameCapture? gameCapture)
    {
        return gameCapture?.Capture()?.Frame;
    }

    /// <summary>
    /// 自动判断当前运行上下文中截图方式，并选择合适的截图方式返回
    /// </summary>
    /// <returns></returns>
    public static ImageRegion CaptureToRectArea(bool forceNew = false)
    {
        var image = CaptureGameImage(TaskTriggerDispatcher.GlobalGameCapture);
        var content = new CaptureContent(image, 0, 0);
        return content.CaptureRectArea;
    }
}
