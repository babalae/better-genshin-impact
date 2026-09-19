using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

// 尘歌壶专用等待。不要改变 TaskControl 的取消异常约定：战斗脚本依赖 NormalEndException。
internal static class SereniteaPotTaskControl
{
    internal static SereniteaPotActiveTime CreateTimer() => new(RunnerContext.Instance.SuspendableDictionary);

    internal static async Task Delay(int milliseconds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureReady(ct);
        await Task.Delay(milliseconds, ct);
        // 暂停或焦点变化可能恰好发生在延迟过程中。
        await EnsureReady(ct);
    }

    private static async Task EnsureReady(CancellationToken ct)
    {
        await CheckPause(ct);
        var heldInputs = SystemControl.IsGenshinImpactActiveByProcess()
            ? Array.Empty<SereniteaPotInputHold>()
            : RunnerContext.Instance.SuspendableDictionary.Values.OfType<SereniteaPotInputHold>()
                .Where(input => !input.IsSuspended).ToArray();
        foreach (var input in heldInputs) input.Suspend();
        // 恢复失败时由外层输入作用域释放按键，不能重新按下。
        GameWindowFocusWaiter.Wait(
            () => User32.IsWindow(TaskContext.Instance().GameHandle),
            SystemControl.IsGenshinImpactActiveByProcess,
            count =>
            {
                if (!TaskContext.Instance().Config.OtherConfig.RestoreFocusOnLostEnabled) return;
                if (count > 0 && count % 10 == 0)
                    SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
                else
                    SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
            }, ct);
        await CheckPause(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var input in heldInputs) input.Resume();
    }

    private static Task CheckPause(CancellationToken ct)
    {
        var context = RunnerContext.Instance;
        return SereniteaPotPauseWaiter.WaitAsync(() => context.IsSuspend,
            () => context.StopAutoPick(), () => context.ResumeAutoPick(), Simulation.ReleaseAllKey,
            context.SuspendableDictionary.Values.ToArray(), Task.Delay, ct,
            e => TaskControl.Logger.LogWarning(e, "尘歌壶暂停状态恢复失败"));
    }

    internal static async Task<bool> WaitForAction(Func<bool> action, CancellationToken ct,
        int retryTimes = 10, int delayMs = 1000)
    {
        for (var attempt = 0; attempt < retryTimes; attempt++)
        {
            await Delay(delayMs, ct);
            ct.ThrowIfCancellationRequested();
            var result = action();
            ct.ThrowIfCancellationRequested();
            if (result) return true;
        }
        return false;
    }
}
