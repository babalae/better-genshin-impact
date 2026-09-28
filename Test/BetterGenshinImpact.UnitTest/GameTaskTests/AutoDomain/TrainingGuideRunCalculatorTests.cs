using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideRunCalculatorTests
{
    [Fact]
    public void BonusAtExactIntegerBoundary_UsesExactArithmetic()
    {
        for (var percent = 0; percent <= TrainingGuideRunCalculator.MaxCraftingBonusReservePercent; percent++)
        {
            TrainingGuideMaterialLevel[] levels =
            [new("低级", 3 * (100 - percent), 0), new("高级", 0, 100)];
            var result = TrainingGuideRunCalculator.Calculate(levels, 0, percent);
            Assert.Equal(percent, result.Levels[0].ReservedBonus);
            Assert.True(result.IsSatisfied);
        }
    }

    [Fact]
    public void HigherLevelSurplus_CannotSatisfyLowerLevelShortage()
    {
        TrainingGuideMaterialLevel[] levels = [new("低级", 0, 1), new("高级", 100, 0)];
        Assert.False(TrainingGuideRunCalculator.Calculate(levels, 0, 15).IsSatisfied);
    }

    [Fact]
    public void RunLimit_IsInclusive()
    {
        TrainingGuideMaterialLevel[] levels = [new("材料", 0, 2, 1)];
        Assert.Null(TrainingGuideRunCalculator.FindMinimumRuns(levels, 0, 1));
        Assert.Equal(2, TrainingGuideRunCalculator.FindMinimumRuns(levels, 0, 2)!.Runs);
    }

    [Fact]
    public void MissingLevel_IsRejectedInsteadOfTreatedAsZero()
    {
        TrainingGuideMaterialLevel[] levels = [null!];
        Assert.Throws<ArgumentException>(() => TrainingGuideRunCalculator.Calculate(levels, 0, 0));
    }

    [Theory]
    [InlineData(0, 7, 7)]
    [InlineData(0, 15, 15)]
    [InlineData(1, 7, 0)]
    public void RunPreference_ResolvesEffectiveReservePercent(
        int preference,
        int configuredPercent,
        int expectedPercent)
    {
        var actual = TrainingGuideRunCalculator.ResolveCraftingBonusReservePercent(
            preference,
            configuredPercent);

        Assert.Equal(expectedPercent, actual);
    }

    [Fact]
    public void TalentFamily_CalculatesThreeLevelsFromLowToHigh()
    {
        TrainingGuideMaterialLevel[] levels =
        [
            new("教导", 0, 0, 3),
            new("指引", 0, 0),
            new("哲学", 0, 1)
        ];

        var result = TrainingGuideRunCalculator.FindMinimumRuns(levels, 0);

        Assert.NotNull(result);
        Assert.Equal(3, result.Runs);
        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void WeaponFamily_CalculatesFourLevelsFromLowToHigh()
    {
        TrainingGuideMaterialLevel[] levels =
        [
            new("低级", 0, 0, 3),
            new("中级", 0, 0),
            new("高级", 0, 0),
            new("最高级", 0, 1)
        ];

        var result = TrainingGuideRunCalculator.FindMinimumRuns(levels, 0);

        Assert.NotNull(result);
        Assert.Equal(9, result.Runs);
    }

    [Fact]
    public void LowLevelRequirement_IsReservedBeforeCrafting()
    {
        TrainingGuideMaterialLevel[] levels =
        [
            new("低级", 8, 3),
            new("中级", 0, 2)
        ];

        var result = TrainingGuideRunCalculator.Calculate(levels, 0, 0);

        Assert.False(result.IsSatisfied);
        Assert.Equal(1, result.Levels[0].Crafted);
        Assert.Equal(1, result.Levels[1].Shortage);
    }

    [Fact]
    public void MiddleLevelSurplus_IsCraftedUpward()
    {
        TrainingGuideMaterialLevel[] levels =
        [
            new("低级", 0, 0),
            new("中级", 7, 1),
            new("高级", 0, 2)
        ];

        var result = TrainingGuideRunCalculator.Calculate(levels, 0, 0);

        Assert.True(result.IsSatisfied);
        Assert.Equal(2, result.Levels[1].Crafted);
        Assert.Equal(2, result.Levels[2].Available);
    }

    [Fact]
    public void RequirementsAtMultipleLevels_AreAllReserved()
    {
        TrainingGuideMaterialLevel[] levels =
        [
            new("低级", 12, 3),
            new("中级", 2, 2),
            new("高级", 1, 2)
        ];

        var result = TrainingGuideRunCalculator.Calculate(levels, 0, 0);

        Assert.True(result.IsSatisfied);
        Assert.Equal(3, result.Levels[0].Crafted);
        Assert.Equal(5, result.Levels[1].Available);
        Assert.Equal(1, result.Levels[1].Crafted);
        Assert.Equal(2, result.Levels[2].Available);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(15, 1)]
    public void ReservePercent_UsesOnlyRealCraftCount(int percent, int expectedBonus)
    {
        TrainingGuideMaterialLevel[] levels =
        [
            new("低级", 30, 0),
            new("中级", 0, 0)
        ];

        var result = TrainingGuideRunCalculator.Calculate(levels, 0, percent);

        Assert.Equal(10, result.Levels[0].Crafted);
        Assert.Equal(expectedBonus, result.Levels[0].ReservedBonus);
    }

    [Fact]
    public void SmallCraftBatch_RoundsReservedBonusDownToZero()
    {
        TrainingGuideMaterialLevel[] levels =
        [
            new("低级", 15, 0),
            new("中级", 0, 0)
        ];

        var result = TrainingGuideRunCalculator.Calculate(levels, 0, 15);

        Assert.Equal(5, result.Levels[0].Crafted);
        Assert.Equal(0, result.Levels[0].ReservedBonus);
    }

    [Fact]
    public void AlreadySatisfied_ReturnsZeroRuns()
    {
        TrainingGuideMaterialLevel[] levels = [new("材料", 10, 9, 1)];

        var result = TrainingGuideRunCalculator.FindMinimumRuns(levels, 7);

        Assert.NotNull(result);
        Assert.Equal(0, result.Runs);
    }

    [Fact]
    public void NoDropForMissingLevel_ReturnsNull()
    {
        TrainingGuideMaterialLevel[] levels = [new("材料", 0, 1)];

        var result = TrainingGuideRunCalculator.FindMinimumRuns(levels, 0, 20);

        Assert.Null(result);
    }
}
