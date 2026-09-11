using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 异常弹窗恢复 / 断网看门狗的纯决策函数。
/// 刻意做到：无 IO、无静态可变状态、无时间获取（时间由调用方传入），便于单元测试。
/// </summary>
public static class ExceptionRecoveryDecisions
{
    /// <summary>是否到了下一次探测时间（intervalSeconds &lt;= 0 表示不节流）。</summary>
    public static bool ShouldProbe(DateTime now, DateTime lastProbeAt, double intervalSeconds)
    {
        if (intervalSeconds <= 0)
        {
            return true;
        }

        return (now - lastProbeAt).TotalSeconds >= intervalSeconds;
    }

    /// <summary>是否允许启动一次新的恢复：既没有恢复在跑（单飞），也不在熔断冷却期内。</summary>
    public static bool ShouldStartRecovery(bool recoveryInFlight, DateTime now, DateTime breakerUntil)
    {
        return !recoveryInFlight && now >= breakerUntil;
    }

    /// <summary>连续失败是否触发熔断（maxAttempts &lt;= 0 表示永不熔断）。</summary>
    public static bool ShouldTripBreaker(int consecutiveFailures, int maxAttempts)
    {
        return maxAttempts > 0 && consecutiveFailures >= maxAttempts;
    }

    /// <summary>ping 失败是否应计入连续失败次数。</summary>
    public static bool ShouldCountPingFailure(bool pingSucceeded)
    {
        return !pingSucceeded;
    }

    /// <summary>
    /// 网络来源挂起的上限（分钟 → TimeSpan）。
    /// 任务线程的等待阶梯（<c>TaskControl.WaitWhileExceptionSuspended</c>）与独立收尾看门狗
    /// （<c>NetworkWatchdogTrigger.ReleaseNetworkSuspensionIfExpired</c>）共用同一口径：
    /// 两处各自 clamp 会在用户调整上限时漂移，而它们必须始终给出同一个界。
    /// </summary>
    public static TimeSpan NetworkSuspendLimit(int configuredMinutes)
    {
        return TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 1, 24 * 60));
    }

    /// <summary>连续 ping 失败是否达到暂停阈值（threshold &lt;= 0 表示不因网络暂停脚本）。</summary>
    public static bool ShouldSuspendByNetwork(int consecutivePingFailures, int threshold)
    {
        return threshold > 0 && consecutivePingFailures >= threshold;
    }

    /// <summary>是否需要走自动重新登录：用户允许重登 且 当前确有登录/进入游戏界面证据。</summary>
    public static bool ShouldRelogin(bool allowRelogin, bool loginScreenDetected)
    {
        return allowRelogin && loginScreenDetected;
    }

    /// <summary>文本是否命中任一关键词（不区分大小写，空关键词忽略）。</summary>
    public static bool MatchesAny(string? text, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text) || keywords.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i];
            if (!string.IsNullOrEmpty(keyword) && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把用户配置的扩展关键词（逗号/分号/竖线/换行分隔）解析为数组，自动去空与去重。
    /// 刻意不把空格当分隔符：国际服关键词本身含空格（如 "Update Notice"）。
    /// </summary>
    public static IReadOnlyList<string> ParseKeywords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var separators = new[] { ',', '，', ';', '；', '|', '\n', '\r' };
        var result = new List<string>();
        foreach (var part in raw.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var keyword = part.Trim();
            if (keyword.Length > 0 && !result.Contains(keyword))
            {
                result.Add(keyword);
            }
        }

        return result;
    }

    /// <summary>把基础关键词与扩展关键词合并成一个查找集合。</summary>
    public static IReadOnlyList<string> MergeKeywords(IReadOnlyList<string> baseKeywords, string? extraRaw)
    {
        var extra = ParseKeywords(extraRaw);
        if (extra.Count == 0)
        {
            return baseKeywords;
        }

        var merged = new List<string>(baseKeywords);
        foreach (var keyword in extra)
        {
            if (!merged.Contains(keyword))
            {
                merged.Add(keyword);
            }
        }

        return merged;
    }
}
