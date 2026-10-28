using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.ResinPlan;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinPlanTests;

/// <summary>体力计划预算计算：各阶段的树脂设置、合成份数与点数折算，不启动游戏。</summary>
public sealed class ResinPlanBudgetTests
{
    /// <summary>0 表示全部，不设预算上限；限定时扣除已消耗的点数，不出现负数。</summary>
    [Fact]
    public void Remaining_AllModeIsUnlimitedAndLimitedModeNeverNegative()
    {
        Assert.Null(ResinPlanBudget.Remaining(0, 500));
        Assert.Equal(40, ResinPlanBudget.Remaining(120, 80));
        Assert.Equal(0, ResinPlanBudget.Remaining(120, 200));
    }

    /// <summary>限定模式先按预算安排浓缩，再按 20 点一档安排原粹，合计不超出预算。</summary>
    [Theory]
    [InlineData(120, 160, 1, 1, 3)]
    [InlineData(30, 160, 2, 0, 1)]
    [InlineData(200, 40, 5, 3, 1)]
    [InlineData(120, 40, null, 0, 2)]
    public void ClaimUsage_LimitedModeStaysWithinBudget(int budget, int original, int? condensed,
        int expectedCondensed, int expectedOriginal)
    {
        var usage = ResinPlanBudget.BuildClaimUsage(budget, new ResinPlanStock(original, 200, condensed));
        Assert.NotNull(usage);
        Assert.Equal(ResinUsageMode.PerType, usage.Mode);
        Assert.Equal(OriginalResinTier.Only20, usage.OriginalTier);
        Assert.Equal(expectedCondensed, usage.Counts.Condensed);
        Assert.Equal(expectedOriginal, usage.Counts.Original);
        Assert.True(usage.Counts.Condensed * 60 + usage.Counts.Original * 20 <= budget);
        Assert.Null(usage.Validate(ResinCapabilities.LeyLineOutcrop));
    }

    /// <summary>预算或持有量不足一次领取时跳过。</summary>
    [Theory]
    [InlineData(10, 160, 2)]
    [InlineData(120, 10, 0)]
    public void ClaimUsage_LimitedModeSkipsWhenNotEnough(int budget, int original, int condensed)
        => Assert.Null(ResinPlanBudget.BuildClaimUsage(budget, new ResinPlanStock(original, 200, condensed)));

    /// <summary>首领只用原粹、不补充，次数受首领设置、预算与持有量共同限制；刷至耗尽时只受预算与持有量限制。</summary>
    [Theory]
    [InlineData(null, 100, 3, 2)]
    [InlineData(40, 160, 3, 1)]
    [InlineData(null, 200, 2, 2)]
    [InlineData(null, 200, null, 5)]
    [InlineData(120, 200, null, 3)]
    public void BossUsage_RespectsRunCountBudgetAndStock(int? budget, int original, int? runLimit, int expected)
    {
        var usage = ResinPlanBudget.BuildBossUsage(budget, new ResinPlanStock(original, 200, 0), runLimit);
        Assert.NotNull(usage);
        Assert.Equal(ResinUsageMode.RunCount, usage.Mode);
        Assert.Equal(expected, usage.RunCount);
        Assert.True(usage.Allowed.Original);
        Assert.False(usage.Allowed.Condensed || usage.Allowed.Transient || usage.Allowed.Fragile);
        Assert.Null(usage.Validate(ResinCapabilities.Boss));
    }

    /// <summary>原粹不足一次讨伐时跳过。</summary>
    [Fact]
    public void BossUsage_SkipsWhenOriginalBelowOneClaim()
        => Assert.Null(ResinPlanBudget.BuildBossUsage(null, new ResinPlanStock(30, 200, 5), 2));

    /// <summary>合成份数先扣除首领预留，不超过持有上限；限定模式下已有浓缩先占用预算；浓缩数量未知时不合成。</summary>
    [Theory]
    [InlineData(null, 200, 0, 0, 3)]
    [InlineData(null, 200, 3, 0, 2)]
    [InlineData(null, 200, 0, 80, 2)]
    [InlineData(null, 50, 0, 0, 0)]
    [InlineData(120, 200, 1, 0, 1)]
    [InlineData(120, 200, 2, 0, 0)]
    public void CraftCount_ReservesBossAndRespectsCapacity(int? budget, int original, int condensed, int reserve, int expected)
        => Assert.Equal(expected, ResinPlanBudget.CraftCount(budget, new ResinPlanStock(original, 200, condensed), reserve));

    /// <summary>浓缩数量未知时不合成。</summary>
    [Fact]
    public void CraftCount_UnknownCondensedDoesNotCraft()
        => Assert.Equal(0, ResinPlanBudget.CraftCount(null, new ResinPlanStock(200, 200, null), 0));

    /// <summary>原粹按点数，浓缩及战前补充的须臾、脆弱每个计 60 点。</summary>
    [Fact]
    public void Points_CountsCondensedAndReplenishAsSixty()
    {
        var report = new ResinUsageReport { Consumed = new() { Original = 80, Condensed = 2, Transient = 1 } };
        Assert.Equal(260, ResinPlanBudget.Points(report));
        Assert.Equal(0, ResinPlanBudget.Points(null));
        Assert.Equal(120, ResinPlanBudget.BossReserve(true, 3));
        Assert.Equal(0, ResinPlanBudget.BossReserve(false, 3));
    }
}
