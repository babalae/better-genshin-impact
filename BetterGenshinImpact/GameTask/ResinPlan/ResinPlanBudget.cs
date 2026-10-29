using System;
using System.Collections.Frozen;
using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划的预算计算：按剩余预算和当前持有量生成各子任务的树脂设置。纯计算，不操作游戏。
/// 预算单位为树脂点数：原粹按实际点数，浓缩每个计 60 点。
/// </summary>
public static class ResinPlanBudget
{
    /// <summary>一个浓缩树脂折合的点数，也是合成一个浓缩消耗的原粹。</summary>
    public const int CondensedPoints = 60;

    /// <summary>秘境、幽境、地脉花领取一次奖励的最小原粹点数。</summary>
    public const int MinClaimPoints = 20;

    /// <summary>首领领取一次奖励的原粹点数。</summary>
    public const int BossClaimPoints = 40;

    /// <summary>浓缩树脂持有上限。</summary>
    public const int CondensedCapacity = 5;

    /// <summary>原粹 40 档领取一次的点数；一次 40 档领取用掉 2 次移涌双倍。</summary>
    public const int OriginalHighTierPoints = 40;

    /// <summary>每个移涌活动每天的双倍次数；今日剩余次数读不到时按此安排。</summary>
    public const int OverflowDailyClaims = 3;

    /// <summary>计算剩余预算。</summary>
    /// <param name="maxResin">本次最多消耗的点数；0 或负数表示全部。</param>
    /// <param name="spentPoints">已经消耗的点数。</param>
    /// <returns>剩余点数；全部模式返回 null，表示只受持有量限制。</returns>
    public static int? Remaining(int maxResin, int spentPoints)
        => maxResin <= 0 ? null : Math.Max(0, maxResin - spentPoints);

    /// <summary>把子任务的树脂报告折算为点数；首领战前补充的须臾、脆弱也按每个 60 点计入。</summary>
    /// <param name="report">子任务报告；子任务未开始时为 null。</param>
    /// <returns>消耗点数。</returns>
    public static int Points(ResinUsageReport? report)
        => report is null
            ? 0
            : report.Consumed.Original + CondensedPoints * (report.Consumed.Condensed + report.Consumed.Transient + report.Consumed.Fragile);

    /// <summary>
    /// 生成秘境、幽境或地脉花阶段的树脂设置。
    /// 全部模式刷至耗尽；限定模式先按预算安排浓缩，再按 20 点一档安排原粹，保证不超出预算。
    /// </summary>
    /// <param name="remaining">剩余预算；null 表示全部模式。</param>
    /// <param name="stock">当前持有量。</param>
    /// <returns>树脂设置；不足领取一次时返回 null。</returns>
    public static ResinUsage? BuildClaimUsage(int? remaining, ResinPlanStock stock)
    {
        if (remaining is not { } budget)
        {
            return stock.CanClaim
                ? new ResinUsage { Mode = ResinUsageMode.Exhaust, Allowed = new() { Condensed = true, Original = true } }
                : null;
        }

        // 1. 浓缩只按已识别的数量安排，每个占用 60 点预算
        var condensed = Math.Min(stock.KnownCondensed, budget / CondensedPoints);
        // 2. 原粹固定使用 20 点档位，受持有量和剩余预算共同限制
        var original = Math.Min(stock.Original / MinClaimPoints, (budget - condensed * CondensedPoints) / MinClaimPoints);
        if (condensed + original <= 0)
        {
            return null;
        }

        return new ResinUsage
        {
            Mode = ResinUsageMode.PerType,
            Counts = new() { Condensed = condensed, Original = original },
            OriginalTier = OriginalResinTier.Only20
        };
    }

    /// <summary>
    /// 体力计划内首领的树脂能力：沿用首领的指定次数与刷至耗尽，只用原粹，不提供须臾、脆弱补充。
    /// 首领子任务设置中的树脂编辑器按此能力展示。
    /// </summary>
    public static ResinUsageCapability BossCapability { get; } = new()
    {
        Modes = new[] { ResinUsageMode.Exhaust, ResinUsageMode.RunCount }.ToFrozenSet(),
        Kinds = new()
        {
            Condensed = ResinKindSupport.Unsupported,
            Original = ResinKindSupport.Direct,
            Transient = ResinKindSupport.Unsupported,
            Fragile = ResinKindSupport.Unsupported
        },
        Tiers = Array.AsReadOnly(new[] { OriginalResinTier.Default }),
        Defaults = new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 1, Allowed = new() { Original = true } },
        Hints = new()
        {
            UnsupportedReasons = new()
            {
                Condensed = "首领讨伐不能使用浓缩树脂",
                Transient = "体力计划只使用原粹与浓缩树脂",
                Fragile = "体力计划只使用原粹与浓缩树脂"
            },
            PerTypeUnsupportedReason = "首领讨伐只按领奖次数控制",
            DefaultTierDescription = "首领固定使用 40 原粹树脂"
        }
    };

    /// <summary>从首领自己的树脂设置取讨伐次数上限：指定次数时为该次数，刷至耗尽时不限制。</summary>
    /// <param name="usage">首领的树脂设置。</param>
    /// <returns>次数上限；null 表示不限制，只受预算与持有量约束。</returns>
    public static int? BossRunLimit(ResinUsage usage)
        => usage.Mode == ResinUsageMode.RunCount ? Math.Max(1, usage.RunCount) : null;

    /// <summary>生成首领阶段的树脂设置：只用原粹，不使用须臾、脆弱补充，次数受剩余预算与持有量限制。</summary>
    /// <param name="remaining">剩余预算；null 表示全部模式。</param>
    /// <param name="stock">当前持有量。</param>
    /// <param name="runLimit">首领设置中的次数上限；null 表示刷至耗尽。</param>
    /// <returns>树脂设置；原粹不足一次讨伐时返回 null。</returns>
    public static ResinUsage? BuildBossUsage(int? remaining, ResinPlanStock stock, int? runLimit)
    {
        var points = remaining is { } budget ? Math.Min(stock.Original, budget) : stock.Original;
        var count = points / BossClaimPoints;
        if (runLimit is { } limit)
        {
            count = Math.Min(limit, count);
        }

        return count <= 0
            ? null
            : new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = count, Allowed = new() { Original = true } };
    }

    /// <summary>
    /// 计算优先合成的浓缩份数：可用原粹先扣除后续只能使用原粹的预留，再按每份 60 点折算，不超过持有上限。
    /// 限定模式下，已有的浓缩先占用预算，避免合成本次用不上的浓缩。
    /// </summary>
    /// <param name="remaining">剩余预算；null 表示全部模式。</param>
    /// <param name="stock">当前持有量；浓缩数量未知时不合成。</param>
    /// <param name="originalReserve">需要为后续阶段保留的原粹点数（例如首领讨伐）。</param>
    /// <returns>合成份数，0 表示不合成。</returns>
    public static int CraftCount(int? remaining, ResinPlanStock stock, int originalReserve)
    {
        if (stock.Condensed is not { } condensed)
        {
            return 0;
        }

        var original = remaining is { } budget
            ? Math.Min(stock.Original, budget - condensed * CondensedPoints)
            : stock.Original;
        // 预留可能为“全部保留”（int.MaxValue），按 long 相减避免溢出
        var available = (long)original - originalReserve;
        if (available < CondensedPoints)
        {
            return 0;
        }

        return (int)Math.Max(0, Math.Min(CondensedCapacity - condensed, available / CondensedPoints));
    }

    /// <summary>
    /// 生成移涌领取的树脂设置：只用原粹，因为浓缩既不享受也不消耗双倍。
    /// 严格关闭时用 40 档领取 ⌈r/2⌉ 次，少打几次；严格开启，或原粹、预算不够 40 档时，用 20 档领取 min(r, ⌊P/20⌋) 次，
    /// 尽量多用双倍。P 为可用于本次领取的原粹：全部模式为持有量，限定模式再受剩余预算限制。
    /// </summary>
    /// <param name="remaining">剩余预算；null 表示全部模式。</param>
    /// <param name="stock">当前持有量。</param>
    /// <param name="doubles">今日剩余双倍次数 r。</param>
    /// <param name="strict">是否严格使用 20 档。</param>
    /// <returns>树脂设置；一次也领不了时返回 null。</returns>
    public static ResinUsage? BuildOverflowUsage(int? remaining, ResinPlanStock stock, int doubles, bool strict)
    {
        if (doubles <= 0)
        {
            return null;
        }

        var points = remaining is { } budget ? Math.Min(stock.Original, budget) : stock.Original;

        // 1. 严格关闭：40 档领取；原粹足够时直接使用，切不到 40 档时子任务会改用 20 档，只会更少
        if (!strict)
        {
            var highTierClaims = (doubles + 1) / 2;
            if (points >= highTierClaims * OriginalHighTierPoints)
            {
                return OriginalOnly(highTierClaims, OriginalResinTier.Prefer40);
            }
        }

        // 2. 严格开启，或原粹不够 40 档：20 档领取，每次用掉 1 次双倍
        var claims = Math.Min(doubles, points / MinClaimPoints);
        return claims > 0 ? OriginalOnly(claims, OriginalResinTier.Only20) : null;
    }

    /// <summary>合成浓缩前为一次移涌领取预留的原粹点数：严格关闭为 40 × ⌈r/2⌉，严格开启为 20 × r。</summary>
    /// <param name="doubles">今日剩余双倍次数 r。</param>
    /// <param name="strict">是否严格使用 20 档。</param>
    /// <returns>预留点数。</returns>
    public static int OverflowReserve(int doubles, bool strict)
        => doubles <= 0 ? 0 : strict ? doubles * MinClaimPoints : (doubles + 1) / 2 * OriginalHighTierPoints;

    /// <summary>饱和加法：两项预留相加超过 int 范围时返回 int.MaxValue（全部保留）。</summary>
    /// <param name="left">第一项。</param>
    /// <param name="right">第二项。</param>
    /// <returns>和；溢出时为 int.MaxValue。</returns>
    public static int SaturatingAdd(int left, int right)
        => (int)Math.Min(int.MaxValue, (long)left + right);

    /// <summary>只用原粹、按指定档位领取若干次的树脂设置。</summary>
    /// <param name="claims">领奖次数。</param>
    /// <param name="tier">原粹档位。</param>
    /// <returns>树脂设置。</returns>
    private static ResinUsage OriginalOnly(int claims, OriginalResinTier tier) => new()
    {
        Mode = ResinUsageMode.PerType,
        Counts = new() { Original = claims },
        OriginalTier = tier
    };

    /// <summary>首领阶段需要预留的原粹点数；首领刷至耗尽时保留全部原粹，不再合成。</summary>
    /// <param name="enabled">首领是否参与。</param>
    /// <param name="runLimit">首领设置中的次数上限；null 表示刷至耗尽。</param>
    /// <returns>预留点数。</returns>
    public static int BossReserve(bool enabled, int? runLimit)
        => !enabled ? 0 : runLimit is { } limit ? Math.Max(0, limit) * BossClaimPoints : int.MaxValue;
}
