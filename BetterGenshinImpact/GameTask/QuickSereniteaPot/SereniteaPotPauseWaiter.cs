using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

internal static class SereniteaPotPauseWaiter
{
    // 只归还本次等待取得的暂停；不能重置其他任务持有的自动拾取计数。
    internal static async Task WaitAsync(Func<bool> isPaused, Action stopAutoPick,
        Action resumeAutoPick, Action releaseInput, IEnumerable<ISuspendable> components,
        Func<int, CancellationToken, Task> delay, CancellationToken ct,
        Action<Exception> reportCleanupFailure)
    {
        ct.ThrowIfCancellationRequested();
        if (!isPaused()) return;
        var owned = new List<ISuspendable>();
        stopAutoPick();
        try
        {
            foreach (var component in components)
            {
                if (component.IsSuspended) continue;
                // Suspend 也可能在改变状态后抛错，必须保留恢复机会。
                owned.Add(component);
                component.Suspend();
            }
            releaseInput();
            while (isPaused())
            {
                await delay(100, ct);
                ct.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            try
            {
                foreach (var component in owned)
                {
                    try { component.Resume(); }
                    catch (Exception e) { reportCleanupFailure(e); }
                }
            }
            finally { resumeAutoPick(); }
        }
        ct.ThrowIfCancellationRequested();
    }
}
