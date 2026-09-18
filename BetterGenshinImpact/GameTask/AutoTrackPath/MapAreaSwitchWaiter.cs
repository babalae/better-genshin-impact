using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>区域菜单可能卡顿十余秒；每轮重新截图识别，超时和取消均不算成功。</summary>
internal static class MapAreaSwitchWaiter
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    internal static async Task<bool> WaitAsync(
        Func<bool> observe,
        Func<int, CancellationToken, Task> delay,
        CancellationToken ct,
        int stableChecks = 1,
        TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        var consecutiveChecks = 0;
        while (clock.GetElapsedTime(started) < Timeout)
        {
            ct.ThrowIfCancellationRequested();
            // 展开菜单后不立刻识别动画前的画面；后续也避免密集 OCR。
            await delay(500, ct);
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) >= Timeout) break;

            var ready = observe();
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) >= Timeout) break;

            consecutiveChecks = ready ? consecutiveChecks + 1 : 0;
            if (consecutiveChecks >= stableChecks) return true;
        }

        ct.ThrowIfCancellationRequested();
        return false;
    }
}
