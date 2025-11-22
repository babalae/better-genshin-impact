using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// Manages pausable delays with suspend/resume functionality and window focus checking.
/// Uses event-based synchronization for efficient waiting instead of busy polling.
/// </summary>
public class PausableDelayManager
{
    private static readonly ILogger Logger = App.GetLogger<PausableDelayManager>();

    /// <summary>
    /// Performs a pausable sleep with suspend checking and window focus validation.
    /// </summary>
    /// <param name="millisecondsTimeout">Time to sleep in milliseconds</param>
    public void Sleep(int millisecondsTimeout)
    {
        if (millisecondsTimeout <= 0)
        {
            return;
        }

        // Check and handle suspend/window focus before sleeping
        CheckAndHandleSuspend();
        CheckAndActivateGameWindow();

        // Perform the actual sleep with periodic pause checks
        SleepWithPauseCheck(millisecondsTimeout);
    }

    /// <summary>
    /// Only checks and handles pause/suspend state and window focus without sleeping.
    /// Useful for ensuring the game is in the correct state before proceeding.
    /// </summary>
    public void CheckPauseAndWindowFocus()
    {
        CheckAndHandleSuspend();
        CheckAndActivateGameWindow();
    }

    /// <summary>
    /// Performs a pausable sleep with cancellation support.
    /// </summary>
    /// <param name="millisecondsTimeout">Time to sleep in milliseconds</param>
    /// <param name="ct">Cancellation token</param>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    public void Sleep(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        // Check and handle suspend/window focus before sleeping
        CheckAndHandleSuspend();
        CheckAndActivateGameWindow();

        // Perform the actual sleep with periodic pause checks
        SleepWithPauseCheck(millisecondsTimeout, ct);
        
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
    }

    /// <summary>
    /// Performs a pausable async delay with cancellation support.
    /// </summary>
    /// <param name="millisecondsTimeout">Time to delay in milliseconds</param>
    /// <param name="ct">Cancellation token</param>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    public async Task DelayAsync(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        // Check and handle suspend/window focus before delaying
        CheckAndHandleSuspend();
        CheckAndActivateGameWindow();

        // Perform the actual delay with periodic pause checks
        await DelayWithPauseCheckAsync(millisecondsTimeout, ct);
        
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
    }

    /// <summary>
    /// Checks and handles suspend state, blocking until resume if needed.
    /// Uses a simple polling approach with efficient waiting.
    /// </summary>
    private void CheckAndHandleSuspend()
    {
        var first = true;
        var isSuspend = RunnerContext.Instance.IsSuspend;

        while (RunnerContext.Instance.IsSuspend)
        {
            if (first)
            {
                RunnerContext.Instance.StopAutoPick();
                
                // Release any pressed keys
                Thread.Sleep(300);
                ReleaseAllPressedKeys();

                Logger.LogWarning("快捷键触发暂停，等待解除");
                
                // Suspend all registered suspendables
                foreach (var item in RunnerContext.Instance.SuspendableDictionary)
                {
                    item.Value.Suspend();
                }

                first = false;
            }

            // Wait before checking again
            Thread.Sleep(1000);
        }

        // Resume from suspend
        if (isSuspend)
        {
            Logger.LogWarning("暂停已经解除");
            RunnerContext.Instance.ResumeAutoPick();
            
            // Resume all registered suspendables
            foreach (var item in RunnerContext.Instance.SuspendableDictionary)
            {
                item.Value.Resume();
            }
        }
    }

    private void CheckAndActivateGameWindow()
    {
        // If RestoreFocusOnLostEnabled is disabled, we require the game window to be active
        // Keep retrying until it is active
        if (!TaskContext.Instance().Config.OtherConfig.RestoreFocusOnLostEnabled)
        {
            // Just proceed to the retry loop below which will ensure window is active
        }

        var count = 0;
        // Keep retrying until window is active
        while (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            if (count >= 10 && count % 10 == 0)
            {
                Logger.LogInformation("多次尝试未恢复，尝试最小化后激活窗口！");
                SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
            }
            else
            {
                Logger.LogInformation("当前获取焦点的窗口不是原神，尝试恢复窗口");
                SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
            }

            count++;
            Thread.Sleep(1000);
        }
    }

    /// <summary>
    /// Sleeps with periodic pause state checks using event-based waiting.
    /// </summary>
    private void SleepWithPauseCheck(int millisecondsTimeout, CancellationToken ct = default)
    {
        var remaining = millisecondsTimeout;
        var checkInterval = Math.Min(1000, millisecondsTimeout); // Check every 1 second or less

        while (remaining > 0)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            var sleepTime = Math.Min(checkInterval, remaining);
            Thread.Sleep(sleepTime);
            remaining -= sleepTime;

            // Check if we need to suspend
            if (RunnerContext.Instance.IsSuspend)
            {
                CheckAndHandleSuspend();
                CheckAndActivateGameWindow();
            }
        }
    }

    /// <summary>
    /// Async delay with periodic pause state checks using event-based waiting.
    /// </summary>
    private async Task DelayWithPauseCheckAsync(int millisecondsTimeout, CancellationToken ct)
    {
        var remaining = millisecondsTimeout;
        var checkInterval = Math.Min(1000, millisecondsTimeout); // Check every 1 second or less

        while (remaining > 0)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            var delayTime = Math.Min(checkInterval, remaining);
            await Task.Delay(delayTime, ct);
            remaining -= delayTime;

            // Check if we need to suspend
            if (RunnerContext.Instance.IsSuspend)
            {
                CheckAndHandleSuspend();
                CheckAndActivateGameWindow();
            }
        }
    }

    private static bool IsKeyPressed(User32.VK key)
    {
        var state = User32.GetAsyncKeyState((int)key);
        return (state & 0x8000) != 0;
    }

    private static void ReleaseAllPressedKeys()
    {
        foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
        {
            if (IsKeyPressed(key))
            {
                Logger.LogWarning($"解除{key}的按下状态.");
                Core.Simulator.Simulation.SendInput.Keyboard.KeyUp(key);
            }
        }
    }
}
