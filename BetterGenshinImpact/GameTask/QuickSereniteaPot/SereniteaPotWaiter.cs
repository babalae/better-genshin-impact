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
    /// <summary>
    /// 在 30 秒内等待读取到洞天名称；复用稳定检查，超时不返回先前的识别结果。
    /// </summary>
    /// <param name="readName">每轮重新截图；地图尚未就绪或识别失败时返回 null。</param>
    /// <param name="delay">接收取消令牌的轮询延迟。</param>
    /// <param name="ct">取消整个等待；同步识别返回后再次检查。</param>
    /// <param name="timeProvider">测试用时钟，默认使用实际经过时间。</param>
    /// <returns>确认成功后的名称；超时返回 null。</returns>
    internal static async Task<string?> WaitForRealmNameAsync(
        Func<string?> readName,
        Func<int, CancellationToken, Task> delay,
        CancellationToken ct,
        TimeProvider? timeProvider = null)
    {
        string? name = null;
        var matchingNames = 0;
        var ready = await WaitAsync(() =>
        {
            var candidate = readName()?.Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                name = null;
                matchingNames = 0;
                return false;
            }
            matchingNames = string.Equals(name, candidate, StringComparison.Ordinal) ? matchingNames + 1 : 1;
            name = candidate;
            return matchingNames >= 3;
        }, delay, ct, TimeSpan.FromSeconds(30), timeProvider: timeProvider, stableChecks: 1);
        return ready ? name : null;
    }

    internal static async Task<bool> WaitAsync(
        Func<bool> isReady,
        Func<int, CancellationToken, Task> delay,
        CancellationToken ct,
        TimeSpan timeout,
        TimeSpan minimumWait = default,
        TimeProvider? timeProvider = null,
        int stableChecks = 3)
    {
        if (stableChecks < 1) throw new ArgumentOutOfRangeException(nameof(stableChecks));
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
            if (stableFrames >= stableChecks)
            {
                return true;
            }
        }

        ct.ThrowIfCancellationRequested();
        return false;
    }
}
