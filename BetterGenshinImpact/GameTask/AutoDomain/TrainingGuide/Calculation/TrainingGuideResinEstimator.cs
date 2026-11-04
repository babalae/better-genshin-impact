using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>按入口实际读取的材料估算树脂；可传入该难度的每20体期望，不推算未读取的高级需求。</summary>
public static class TrainingGuideResinEstimator
{
    public static int? Estimate(IReadOnlyList<TrainingGuideMaterialReading> readings, int reservePercent,
        int resinPerClaim, IReadOnlyList<decimal>? expectedDropsPer20 = null)
    {
        if (resinPerClaim is not (20 or 40 or 60)) throw new ArgumentOutOfRangeException(nameof(resinPerClaim));
        if (reservePercent is < 0 or > TrainingGuideRunCalculator.MaxCraftingBonusReservePercent) throw new ArgumentOutOfRangeException(nameof(reservePercent));
        var targets = readings.Where(r => r.IsTarget).ToArray();
        if (targets.Length == 0) return 0;
        if (readings.Select(r => r.Material.Family).Distinct().Count() != 1) return null;
        var maxTier = targets.Max(r => r.Material.Tier);
        var levels = new TrainingGuideMaterialReading[maxTier + 1];
        for (var tier = 0; tier <= maxTier; tier++)
        {
            var candidates = readings.Where(r => r.Material.Tier == tier).Distinct().ToArray();
            if (candidates.Length != 1) return null;
            levels[tier] = candidates[0];
        }
        var drops = TrainingGuideDropExpectations.Per20(targets[0].Material.IsWeapon);
        if (expectedDropsPer20 != null)
        {
            if (expectedDropsPer20.Count != drops.Length || expectedDropsPer20.Any(value => value < 0))
                throw new ArgumentException("掉落期望与材料等级不匹配", nameof(expectedDropsPer20));
            drops = expectedDropsPer20.ToArray();
        }
        var calculationLevels = levels.Select((level, tier) => new TrainingGuideMaterialLevel(
            level.Material.Name, level.Stock, level.Required, drops[tier] * resinPerClaim / 20m)).ToArray();
        var runs = TrainingGuideRunCalculator.FindMinimumRuns(calculationLevels, reservePercent);
        return runs is int count ? checked(count * resinPerClaim) : null;
    }
}
