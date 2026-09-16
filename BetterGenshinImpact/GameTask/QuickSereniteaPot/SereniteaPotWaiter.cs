using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

/// <summary>
/// 等待连续稳定的识别结果；超时和取消均不能当作成功。
/// 时间和延迟可替换，便于用录制的状态序列测试，无需操作游戏。
/// </summary>
internal static class SereniteaPotWaiter
{
    internal static async Task<bool> WaitAsync(
        Func<bool> isReady,
        Func<int, CancellationToken, Task> delay,
        CancellationToken ct,
        TimeSpan timeout,
        TimeSpan minimumWait = default,
        TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        var stableFrames = 0;
        while (clock.GetElapsedTime(started) < timeout)
        {
            ct.ThrowIfCancellationRequested();
            await delay(500, ct);
            ct.ThrowIfCancellationRequested();
            var elapsed = clock.GetElapsedTime(started);
            if (elapsed >= timeout)
            {
                return false;
            }

            if (elapsed < minimumWait)
            {
                continue;
            }

            var ready = isReady();
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) >= timeout)
            {
                return false;
            }
            stableFrames = ready ? stableFrames + 1 : 0;
            if (stableFrames >= 3)
            {
                return true;
            }
        }

        ct.ThrowIfCancellationRequested();
        return false;
    }
}
