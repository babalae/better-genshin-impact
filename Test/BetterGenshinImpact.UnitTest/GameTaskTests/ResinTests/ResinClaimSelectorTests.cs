using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>验证公共选择规则、领奖与补充计数及报告快照。</summary>
public sealed class ResinClaimSelectorTests
{
    /// <summary>耗尽方式支持须臾和脆弱，顺序固定，界面未提供或本次关闭的种类被过滤。</summary>
    [Fact]
    public void Exhaust_CandidatesFollowOrderAndFilterOfferedAndAllowed()
    {
        var usage = new ResinUsage { Allowed = new() { Condensed = true, Original = true, Transient = true, Fragile = true } };
        var selector = new ResinClaimSelector(usage, ResinCapabilities.Domain);
        var offered = new HashSet<ResinKind> { ResinKind.Fragile, ResinKind.Transient, ResinKind.Original, ResinKind.Condensed };
        Assert.Equal(ResinKinds.InOrder, selector.Candidates(offered));
        offered.Remove(ResinKind.Original);
        Assert.Equal(new[] { ResinKind.Condensed, ResinKind.Transient, ResinKind.Fragile }, selector.Candidates(offered));
        Assert.Empty(selector.Candidates(new HashSet<ResinKind>()));
        Assert.False(selector.IsTargetReached);
        Assert.Empty(selector.ReplenishKinds);

        usage.Allowed.Fragile = false;
        var filtered = new ResinClaimSelector(usage, ResinCapabilities.Domain);
        Assert.Equal(new[] { ResinKind.Condensed, ResinKind.Transient }, filtered.Candidates(offered));
    }

    /// <summary>指定次数只在成功领奖后增加，达标时没有候选，报告包含实际点数和个数。</summary>
    [Fact]
    public void RunCount_ReachesTargetAndBuildsAccurateReport()
    {
        var usage = new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 3 };
        var selector = new ResinClaimSelector(usage, ResinCapabilities.Domain);
        var offered = ResinKinds.InOrder.ToHashSet();
        Assert.False(selector.IsTargetReached);
        Assert.Equal(0, selector.ClaimCount);
        selector.RecordClaim(ResinKind.Condensed, 0);
        selector.RecordClaim(ResinKind.Original, 40);
        Assert.False(selector.IsTargetReached);
        selector.RecordClaim(ResinKind.Original, 20);
        Assert.True(selector.IsTargetReached);
        Assert.Empty(selector.Candidates(offered));
        var report = selector.BuildReport(ResinStopReason.TargetReached);
        Assert.Equal(ResinUsageMode.RunCount, report.Mode);
        Assert.Equal(3, report.ClaimCount);
        Assert.Equal(3, report.TargetCount);
        Assert.Null(report.TargetCounts);
        Assert.Equal(new ResinKindValues<int> { Condensed = 1, Original = 2 }, report.Claims);
        Assert.Equal(new ResinKindValues<int> { Condensed = 1, Original = 60 }, report.Consumed);
        Assert.Equal(ResinStopReason.TargetReached, report.StopReason);
    }

    /// <summary>树脂不足或没有界面选项时可提前结束，报告保留未完成的目标。</summary>
    [Theory]
    [InlineData(ResinStopReason.ResinExhausted)]
    [InlineData(ResinStopReason.NoUsableOption)]
    public void RunCount_ReportsEarlyStopWithoutPretendingTargetReached(ResinStopReason reason)
    {
        var selector = new ResinClaimSelector(new() { Mode = ResinUsageMode.RunCount, RunCount = 6 }, ResinCapabilities.Domain);
        selector.RecordClaim(ResinKind.Condensed, 0);
        Assert.Empty(selector.Candidates(new HashSet<ResinKind>()));
        Assert.False(selector.IsTargetReached);
        var report = selector.BuildReport(reason);
        Assert.Equal(1, report.ClaimCount);
        Assert.Equal(6, report.TargetCount);
        Assert.Equal(reason, report.StopReason);
    }

    /// <summary>分别指定时完成的种类不再候选，前一类不在界面时仍可跳到下一类。</summary>
    [Fact]
    public void PerType_SkipsCompletedAndUnavailableKinds()
    {
        var selector = new ResinClaimSelector(new()
        {
            Mode = ResinUsageMode.PerType, Allowed = new(), Counts = new() { Condensed = 1, Original = 2, Transient = 1 }
        }, ResinCapabilities.Domain);
        var offered = ResinKinds.InOrder.ToHashSet();
        Assert.Equal(new[] { ResinKind.Condensed, ResinKind.Original, ResinKind.Transient }, selector.Candidates(offered));
        Assert.Equal(new[] { ResinKind.Original }, selector.Candidates(new HashSet<ResinKind> { ResinKind.Original }));
        selector.RecordClaim(ResinKind.Condensed, 0);
        Assert.Equal(new[] { ResinKind.Original, ResinKind.Transient }, selector.Candidates(offered));
        Assert.Equal(new ResinKindValues<int> { Original = 2, Transient = 1 }, selector.RemainingCounts);
        selector.RecordClaim(ResinKind.Original, 40);
        selector.RecordClaim(ResinKind.Original, 20);
        Assert.Equal(new[] { ResinKind.Transient }, selector.Candidates(offered));
        selector.RecordClaim(ResinKind.Transient, 0);
        Assert.True(selector.IsTargetReached);
        Assert.Empty(selector.Candidates(offered));
        Assert.Equal(new ResinKindValues<int>(), selector.RemainingCounts);
        var report = selector.BuildReport(ResinStopReason.TargetReached);
        Assert.Equal(4, report.TargetCount);
        Assert.Equal(new ResinKindValues<int> { Condensed = 1, Original = 2, Transient = 1 }, report.TargetCounts);
        Assert.Equal(new ResinKindValues<int> { Condensed = 1, Original = 60, Transient = 1 }, report.Consumed);
    }

    /// <summary>首领补充按须臾、脆弱排序，补充消耗不计入领奖次数，也不能进入直接候选。</summary>
    [Theory]
    [InlineData(ResinUsageMode.Exhaust)]
    [InlineData(ResinUsageMode.RunCount)]
    public void Boss_SeparatesReplenishmentFromDirectClaims(ResinUsageMode mode)
    {
        var usage = ResinCapabilities.Boss.Defaults;
        usage.Mode = mode;
        usage.RunCount = 6;
        usage.Allowed.Transient = true;
        usage.Allowed.Fragile = true;
        var selector = new ResinClaimSelector(usage, ResinCapabilities.Boss);
        Assert.Equal(new[] { ResinKind.Transient, ResinKind.Fragile }, selector.ReplenishKinds);
        Assert.Equal(new[] { ResinKind.Original }, selector.Candidates(ResinKinds.InOrder.ToHashSet()));
        selector.RecordConsumed(ResinKind.Transient, 2);
        selector.RecordConsumed(ResinKind.Fragile, 1);
        Assert.Equal(0, selector.ClaimCount);
        selector.RecordClaim(ResinKind.Original, 40);
        var report = selector.BuildReport(ResinStopReason.ResinExhausted);
        Assert.Equal(new ResinKindValues<int> { Original = 1 }, report.Claims);
        Assert.Equal(new ResinKindValues<int> { Original = 40, Transient = 2, Fragile = 1 }, report.Consumed);
        Assert.Equal(mode == ResinUsageMode.RunCount ? 6 : (int?)null, report.TargetCount);
        usage.Allowed.Transient = false;
        Assert.Equal(new[] { ResinKind.Fragile }, new ResinClaimSelector(usage, ResinCapabilities.Boss).ReplenishKinds);
        usage.Allowed.Fragile = false;
        Assert.Empty(new ResinClaimSelector(usage, ResinCapabilities.Boss).ReplenishKinds);
    }

    /// <summary>耗尽方式永远不自行达标，非原粹直接使用按个数记账。</summary>
    [Fact]
    public void Exhaust_HasNoTargetAndRecordsEveryDirectKind()
    {
        var usage = new ResinUsage { Allowed = new() { Condensed = true, Original = true, Transient = true, Fragile = true } };
        var selector = new ResinClaimSelector(usage, ResinCapabilities.Domain);
        foreach (var kind in ResinKinds.InOrder)
            selector.RecordClaim(kind, kind == ResinKind.Original ? 40 : 0);
        Assert.False(selector.IsTargetReached);
        var report = selector.BuildReport(ResinStopReason.ResinExhausted);
        Assert.Null(report.TargetCount);
        Assert.Equal(4, report.ClaimCount);
        Assert.Equal(new ResinKindValues<int> { Condensed = 1, Original = 1, Transient = 1, Fragile = 1 }, report.Claims);
        Assert.Equal(new ResinKindValues<int> { Condensed = 1, Original = 40, Transient = 1, Fragile = 1 }, report.Consumed);
    }
}
