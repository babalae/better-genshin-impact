using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Material;

/// <summary>材料逐级保留与升级的纯数量计算，不扣除实际库存或摩拉，也不执行合成。</summary>
public static class MaterialFamilyCalculator
{
    /// <summary>沿用培养指南允许设置的最大额外合成收益预留率。</summary>
    public const int MaxCraftingBonusReservePercent = 20;

    /// <summary>各阶先保留自身目标，剩余数量以 3:1 向上合成；额外收益属于估算。</summary>
    public static decimal[] CalculateAvailable(IReadOnlyList<(decimal Stock, decimal Required)> levels, int reservePercent)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (reservePercent is < 0 or > MaxCraftingBonusReservePercent)
            throw new ArgumentOutOfRangeException(nameof(reservePercent));
        var available = new decimal[levels.Count];
        decimal carry = 0;
        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i].Stock < 0 || levels[i].Required < 0)
                throw new ArgumentException("库存和需求不得为负数");
            available[i] = decimal.Floor(levels[i].Stock + carry);
            var crafted = decimal.Floor(Math.Max(available[i] - levels[i].Required, 0) / 3);
            carry = crafted + decimal.Floor(crafted * reservePercent / (100 - reservePercent));
        }
        return available;
    }
}
