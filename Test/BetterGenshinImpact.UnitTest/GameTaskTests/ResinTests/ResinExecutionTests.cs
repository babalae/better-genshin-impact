using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>验证执行层库存预测、批次目标及结束结果，不运行游戏。</summary>
public sealed class ResinExecutionTests
{
    /// <summary>预测扣除本次消耗，只考虑本次允许的树脂，识别失败时继续而不是误判耗尽。</summary>
    [Fact]
    public void LastRound_UsesAllowedKindsAndPostClaimInventory()
    {
        var usage = new ResinUsage { Allowed = new() { Condensed = true } };
        var selector = new ResinClaimSelector(usage, ResinCapabilities.Domain);
        selector.RecordClaim(ResinKind.Condensed, 0);
        Assert.True(ResinExecutionRules.IsLastRound(usage, selector, 200, 1, ResinKind.Condensed, 0));
        Assert.False(ResinExecutionRules.IsLastRound(usage, selector, null, null, ResinKind.Condensed, 0));
        usage.Allowed.Original = true;
        Assert.False(ResinExecutionRules.IsLastRound(usage, selector, 20, 1, ResinKind.Condensed, 0));
        Assert.True(ResinExecutionRules.IsLastRound(usage, selector, 19, 1, ResinKind.Condensed, 0));
        usage.Allowed.Transient = true;
        Assert.False(ResinExecutionRules.IsLastRound(usage, selector, 0, 1, ResinKind.Condensed, 0));
    }

    /// <summary>达标立即停止；分别指定只有尚未完成的种类能使任务继续。</summary>
    [Fact]
    public void LastRound_RespectsTargetsAndPerTypeRemainders()
    {
        var run = new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 1 };
        var selector = new ResinClaimSelector(run, ResinCapabilities.Domain);
        selector.RecordClaim(ResinKind.Original, 40);
        Assert.True(ResinExecutionRules.IsLastRound(run, selector, null, null, ResinKind.Original, 40));
        var perType = new ResinUsage { Mode = ResinUsageMode.PerType, Counts = new() { Condensed = 1, Original = 1 } };
        selector = new ResinClaimSelector(perType, ResinCapabilities.Domain);
        selector.RecordClaim(ResinKind.Condensed, 0);
        Assert.True(ResinExecutionRules.IsLastRound(perType, selector, 0, 99, ResinKind.Condensed, 0));
        Assert.False(ResinExecutionRules.IsLastRound(perType, selector, 20, 99, ResinKind.Condensed, 0));
    }

    /// <summary>按模式限制批次目标，已完成领奖从剩余总目标中扣除。</summary>
    [Theory]
    [InlineData(ResinUsageMode.Exhaust, 10, 2, 10)]
    [InlineData(ResinUsageMode.RunCount, 10, 2, 4)]
    [InlineData(ResinUsageMode.RunCount, 3, 0, 3)]
    [InlineData(ResinUsageMode.PerType, 10, 2, 4)]
    [InlineData(ResinUsageMode.PerType, 3, 0, 3)]
    public void LeyLine_TargetRunCountFollowsMode(ResinUsageMode mode, int available, int claims, int expected)
    {
        var usage = new ResinUsage { Mode = mode, RunCount = 6, Counts = new() { Condensed = 2, Original = 4 } };
        Assert.Equal(expected, ResinExecutionRules.TargetRunCount(usage, available, claims));
    }

    /// <summary>预计算忽略关闭种类；分别指定忽略 allowed，且每类不超过剩余配额。</summary>
    [Fact]
    public void LeyLine_AvailabilityRespectsAllowedCountsAndTiers()
    {
        var inventory = new ResinKindValues<int> { Original = 100, Condensed = 5, Transient = 9, Fragile = 8 };
        var usage = new ResinUsage { Allowed = new() { Original = true }, OriginalTier = OriginalResinTier.Prefer40 };
        Assert.Equal(new ResinKindValues<int> { Original = 3 }, ResinExecutionRules.AvailableClaims(usage, inventory));
        usage.OriginalTier = OriginalResinTier.Only20;
        Assert.Equal(5, ResinExecutionRules.AvailableClaims(usage, inventory).Original);
        usage.Mode = ResinUsageMode.PerType;
        usage.Counts = new() { Original = 4, Condensed = 2, Transient = 1 };
        Assert.Equal(new ResinKindValues<int> { Original = 4, Condensed = 2, Transient = 1 }, ResinExecutionRules.AvailableClaims(usage, inventory));
        Assert.Equal(new ResinKindValues<int> { Original = 1 }, ResinExecutionRules.AvailableClaims(usage, inventory, new() { Original = 1 }));
    }

    /// <summary>默认与优先 40 使用地脉花旧公式，只有只用 20 才按 20 点逐次计算。</summary>
    [Theory]
    [InlineData(OriginalResinTier.Default, 200, 5)]
    [InlineData(OriginalResinTier.Default, 39, 1)]
    [InlineData(OriginalResinTier.Default, 19, 0)]
    [InlineData(OriginalResinTier.Prefer40, 100, 3)]
    [InlineData(OriginalResinTier.Only20, 200, 10)]
    public void LeyLine_OriginalEstimatePreservesLegacyFormula(OriginalResinTier tier, int original, int expected)
    {
        var usage = new ResinUsage { OriginalTier = tier };
        Assert.Equal(expected, ResinExecutionRules.AvailableClaims(usage, new() { Original = original }).Original);
    }

    /// <summary>可解释的不足与没有可解释 OCR 信息分别报告为耗尽和没有选项。</summary>
    [Fact]
    public void NoCandidates_DistinguishesShortageFromRecognitionFailure()
    {
        Assert.Equal(ResinStopReason.NoUsableOption, ResinExecutionRules.NoCandidateReason(new HashSet<ResinKind>(), false));
        Assert.Equal(ResinStopReason.ResinExhausted, ResinExecutionRules.NoCandidateReason(new HashSet<ResinKind>(), true));
        Assert.Equal(ResinStopReason.ResinExhausted, ResinExecutionRules.NoCandidateReason(new HashSet<ResinKind> { ResinKind.Original }, false));
    }
}
