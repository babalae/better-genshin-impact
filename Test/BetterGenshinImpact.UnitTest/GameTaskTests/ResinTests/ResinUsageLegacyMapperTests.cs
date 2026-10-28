using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>逐行验证旧字段映射及用于兼容 getter 的反向投影。</summary>
public sealed class ResinUsageLegacyMapperTests
{
    /// <summary>秘境、幽境旧开关分别映射为耗尽或分别指定，次数和默认开关保留。</summary>
    [Theory]
    [InlineData(false, ResinUsageMode.Exhaust)]
    [InlineData(true, ResinUsageMode.PerType)]
    public void DomainAndStygian_MapModeAndCounts(bool specify, ResinUsageMode mode)
    {
        var stygian = ResinUsageLegacyMapper.FromStygian(specify, 4, 2, 1, 3);
        var domain = ResinUsageLegacyMapper.FromDomain(specify, 4, 2, 1, 3, 0, 0, out var warning);
        Assert.Equal(stygian, domain);
        Assert.Null(warning);
        Assert.Equal(mode, domain.Mode);
        Assert.Equal(new ResinKindValues<int> { Condensed = 2, Original = 4, Transient = 1, Fragile = 3 }, domain.Counts);
        Assert.Equal(new ResinKindValues<bool> { Condensed = true, Original = true }, domain.Allowed);
        Assert.Equal(OriginalResinTier.Default, domain.OriginalTier);
        Assert.Equal(specify, ResinUsageLegacyMapper.ToSpecifyResinUse(domain));
    }

    /// <summary>秘境的单独 20、单独 40、彼此混用和与不分档位混用分别按设计归一化。</summary>
    [Theory]
    [InlineData(0, 0, 3, 3, OriginalResinTier.Prefer40, false)]
    [InlineData(0, 2, 0, 2, OriginalResinTier.Only20, false)]
    [InlineData(0, 2, 3, 5, OriginalResinTier.Default, true)]
    [InlineData(4, 2, 0, 6, OriginalResinTier.Default, true)]
    [InlineData(4, 0, 3, 7, OriginalResinTier.Default, true)]
    [InlineData(4, 2, 3, 9, OriginalResinTier.Default, true)]
    public void Domain_CombinesOriginalTiersAndWarnsOnMixedQuotas(int original, int original20, int original40,
        int expectedCount, OriginalResinTier expectedTier, bool hasWarning)
    {
        var usage = ResinUsageLegacyMapper.FromDomain(true, original, 2, 1, 3, original20, original40, out var warning);
        Assert.Equal(expectedCount, usage.Counts.Original);
        Assert.Equal(expectedTier, usage.OriginalTier);
        Assert.Equal(hasWarning, warning is not null);
        if (hasWarning)
            Assert.Contains("合并领奖次数", warning);
    }

    /// <summary>旧档位字段即使隐藏也保留映射，但不会把耗尽方式改成分别指定。</summary>
    [Fact]
    public void Domain_InactiveTierCountsDoNotChangeMode()
    {
        var usage = ResinUsageLegacyMapper.FromDomain(false, 0, 0, 0, 0, 0, 2, out var warning);
        Assert.Equal(ResinUsageMode.Exhaust, usage.Mode);
        Assert.Equal(2, usage.Counts.Original);
        Assert.Equal(OriginalResinTier.Default, usage.OriginalTier);
        Assert.Null(warning);
    }

    /// <summary>首领的两种方式都保留须臾、脆弱补充开关，并且只允许原粹直接领奖。</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Boss_PreservesSupplementSwitches(bool specify, bool transient, bool fragile)
    {
        var usage = ResinUsageLegacyMapper.FromBoss(specify, 6, transient, fragile);
        Assert.Equal(specify ? ResinUsageMode.RunCount : ResinUsageMode.Exhaust, usage.Mode);
        Assert.Equal(6, usage.RunCount);
        Assert.Equal(new ResinKindValues<bool> { Original = true, Transient = transient, Fragile = fragile }, usage.Allowed);
        Assert.Equal(specify, ResinUsageLegacyMapper.ToBossSpecifyRunCount(usage));
        Assert.Null(usage.Validate(ResinCapabilities.Boss));
    }

    /// <summary>地脉花手动、耗尽取小值、纯耗尽三行映射完整，旧开关的任意组合都确定。</summary>
    [Theory]
    [InlineData(false, false, ResinUsageMode.RunCount)]
    [InlineData(false, true, ResinUsageMode.RunCount)]
    [InlineData(true, true, ResinUsageMode.RunCount)]
    [InlineData(true, false, ResinUsageMode.Exhaust)]
    public void LeyLine_NormalizesLegacyModeCombination(bool exhaustion, bool min, ResinUsageMode expectedMode)
    {
        var usage = ResinUsageLegacyMapper.FromLeyLine(exhaustion, min, 6, true, false);
        Assert.Equal(expectedMode, usage.Mode);
        Assert.Equal(6, usage.RunCount);
        Assert.Equal(new ResinKindValues<bool> { Condensed = true, Original = true, Transient = true }, usage.Allowed);

        // 反向投影使用一个规范组合，无法恢复旧“耗尽且取小值”的原始开关组合。
        var projected = ResinUsageLegacyMapper.FromLeyLine(
            ResinUsageLegacyMapper.ToLeyLineIsResinExhaustionMode(usage),
            ResinUsageLegacyMapper.ToLeyLineOpenModeCountMin(usage), usage.RunCount,
            usage.Allowed.Transient, usage.Allowed.Fragile);
        Assert.Equal(usage, projected);
    }

    /// <summary>非正保留量均归一化为默认合成，正数不变。</summary>
    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    public void Craft_NormalizesNonPositiveValues(int value, int expected)
        => Assert.Equal(expected, ResinUsageLegacyMapper.NormalizeMinResinToKeep(value));
}
