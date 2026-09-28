using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>
/// 培养计划的纯计算器。它不访问配置、截图或输入设备，可安全用于扫描后的估算和领奖后的重算。
/// </summary>
public static class TrainingGuideRunCalculator
{
    public const int MaxCraftingBonusReservePercent = 20;

    /// <summary>
    /// 根据刷取偏好得到本次计算实际使用的预留率。
    /// 偏好 0 使用用户配置；偏好 1 必须完全依赖库存、掉落和普通合成，因此返回 0。
    /// </summary>
    public static int ResolveCraftingBonusReservePercent(int runPreference, int configuredPercent)
    {
        if (configuredPercent is < 0 or > MaxCraftingBonusReservePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredPercent),
                $"合成收益预留率必须在 0～{MaxCraftingBonusReservePercent} 之间");
        }

        return runPreference switch
        {
            0 => configuredPercent,
            1 => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(runPreference), "未知的培养计划刷取偏好")
        };
    }

    /// <summary>
    /// 顺序枚举首次满足全部等级需求的刷取次数。无法在上限内满足时返回 null。
    /// </summary>
    public static TrainingGuideRunCalculation? FindMinimumRuns(
        IReadOnlyList<TrainingGuideMaterialLevel> levels,
        int craftingBonusReservePercent,
        int maxRuns = 10_000)
    {
        Validate(levels, craftingBonusReservePercent, maxRuns);

        for (var runs = 0; ; runs++)
        {
            var calculation = Calculate(levels, runs, craftingBonusReservePercent);
            if (calculation.IsSatisfied)
            {
                return calculation;
            }
            if (runs == maxRuns)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 模拟指定刷取次数后的逐级保留与 3:1 合成。
    /// </summary>
    public static TrainingGuideRunCalculation Calculate(
        IReadOnlyList<TrainingGuideMaterialLevel> levels,
        int runs,
        int craftingBonusReservePercent)
    {
        Validate(levels, craftingBonusReservePercent, runs);

        var carry = 0;
        var allSatisfied = true;
        var results = new List<TrainingGuideLevelCalculation>(levels.Count);

        for (var i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
            var availableLong = level.Stock + level.ExpectedDropPerRun * runs + carry;
            if (availableLong > int.MaxValue)
            {
                throw new OverflowException("材料数量超出支持范围");
            }

            var available = (int)decimal.Floor(availableLong);
            var shortage = Math.Max(level.Required - available, 0);
            allSatisfied &= shortage == 0;

            var surplus = Math.Max(available - level.Required, 0);
            var crafted = i == levels.Count - 1 ? 0 : surplus / 3;
            // 百分比为整数，使用有理数计算以避免整数边界上的浮点误差。
            var reservedBonus = (int)((long)crafted * craftingBonusReservePercent /
                                      (100 - craftingBonusReservePercent));
            carry = crafted + reservedBonus;

            results.Add(new TrainingGuideLevelCalculation(
                level.Name,
                available,
                level.Required,
                shortage,
                crafted,
                reservedBonus));
        }

        return new TrainingGuideRunCalculation(runs, allSatisfied, results);
    }

    private static void Validate(
        IReadOnlyList<TrainingGuideMaterialLevel> levels,
        int craftingBonusReservePercent,
        int nonNegativeValue)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0)
        {
            throw new ArgumentException("材料家族至少需要包含一个等级", nameof(levels));
        }

        if (craftingBonusReservePercent is < 0 or > MaxCraftingBonusReservePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(craftingBonusReservePercent),
                $"合成收益预留率必须在 0～{MaxCraftingBonusReservePercent} 之间");
        }

        if (nonNegativeValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nonNegativeValue));
        }

        foreach (var level in levels)
        {
            if (level is null)
            {
                throw new ArgumentException("材料等级数据不可缺失", nameof(levels));
            }
            if (level.Stock < 0 || level.Required < 0 || level.ExpectedDropPerRun < 0)
            {
                throw new ArgumentException("库存、需求和预计掉落均不得为负数", nameof(levels));
            }
        }
    }
}
