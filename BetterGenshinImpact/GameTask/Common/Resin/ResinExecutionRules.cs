using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>执行层共用的库存预测与目标计算；不识图、不修改参数。</summary>
public static class ResinExecutionRules
{
    /// <summary>判断本次仍可能使用某类树脂；分别指定时读取剩余次数，其余方式读取开关。</summary>
    public static bool CanUse(ResinUsage usage, ResinClaimSelector selector, ResinKind kind)
        => usage.Mode == ResinUsageMode.PerType ? selector.RemainingCounts[kind] > 0 : usage.Allowed[kind];

    /// <summary>以领奖前顶栏库存预测是否最后一轮；识别失败或补充树脂余量未知时继续。</summary>
    /// <param name="usage">本次设置。</param>
    /// <param name="selector">已记录本次成功领奖的选择器。</param>
    /// <param name="originalBefore">领奖前原粹点数；识别失败为 null。</param>
    /// <param name="condensedBefore">领奖前浓缩个数；识别失败为 null。</param>
    /// <param name="claimedKind">本次使用种类。</param>
    /// <param name="originalSpent">本次消耗原粹点数。</param>
    public static bool IsLastRound(ResinUsage usage, ResinClaimSelector selector, int? originalBefore,
        int? condensedBefore, ResinKind claimedKind, int originalSpent)
    {
        if (selector.IsTargetReached) return true;
        // 领奖弹窗看不到须臾、脆弱库存，开启后可能多打一轮，下一次没有候选时才结束。
        if (CanUse(usage, selector, ResinKind.Transient) || CanUse(usage, selector, ResinKind.Fragile)) return false;
        if (originalBefore is null || condensedBefore is null) return false;
        var original = originalBefore.Value - (claimedKind == ResinKind.Original ? originalSpent : 0);
        var condensed = condensedBefore.Value - (claimedKind == ResinKind.Condensed ? 1 : 0);
        return !(CanUse(usage, selector, ResinKind.Condensed) && condensed > 0)
            && !(CanUse(usage, selector, ResinKind.Original) && original >= 20);
    }

    /// <summary>没有候选时，有树脂行或明确不足提示表示库存不足；OCR 没有可解释信息时表示未找到选项。</summary>
    public static ResinStopReason NoCandidateReason(IReadOnlySet<ResinKind> offered, bool shortageDetected)
        => offered.Count > 0 || shortageDetected ? ResinStopReason.ResinExhausted : ResinStopReason.NoUsableOption;

    /// <summary>将库存转换为允许使用的领奖次数，分别指定时各类不超过剩余配额。</summary>
    /// <param name="usage">本次设置。</param>
    /// <param name="inventory">库存，原粹为点数，其他为个数。</param>
    /// <param name="remaining">分别指定时的剩余配额；为空时使用初始配额。</param>
    public static ResinKindValues<int> AvailableClaims(ResinUsage usage, ResinKindValues<int> inventory,
        ResinKindValues<int>? remaining = null)
    {
        var available = new ResinKindValues<int>();
        foreach (var kind in ResinKinds.InOrder)
        {
            var count = Math.Max(0, inventory[kind]);
            // 默认与优先 40 沿用地脉花旧公式：先按 40 点计算，余量够 20 点再计一次，避免耗尽模式白跑。
            if (kind == ResinKind.Original)
                count = usage.OriginalTier == OriginalResinTier.Only20 ? count / 20 : count / 40 + (count % 40 >= 20 ? 1 : 0);
            available[kind] = usage.Mode == ResinUsageMode.PerType
                ? Math.Min(count, (remaining ?? usage.Counts)[kind]) : usage.Allowed[kind] ? count : 0;
        }
        return available;
    }

    /// <summary>计算本次预计算批次可继续领奖的次数；总次数和分别指定均扣除已完成目标。</summary>
    public static int TargetRunCount(ResinUsage usage, int availableClaims, int claimCount = 0)
        => usage.Mode switch
        {
            ResinUsageMode.RunCount => Math.Min(availableClaims, Math.Max(0, usage.RunCount - claimCount)),
            ResinUsageMode.PerType => Math.Min(availableClaims, Math.Max(0, ResinKinds.InOrder.Sum(kind => usage.Counts[kind]) - claimCount)),
            _ => availableClaims
        };
}
