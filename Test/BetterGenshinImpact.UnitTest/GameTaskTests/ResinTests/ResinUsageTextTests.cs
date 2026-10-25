using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>验证设计规定的摘要、报告、无效提示及名称文案。</summary>
public sealed class ResinUsageTextTests
{
    /// <summary>三种摘要和非默认档位的文字及分隔符严格一致。</summary>
    [Fact]
    public void Summary_UsesSharedWordingAndTierSuffix()
    {
        var usage = new ResinUsage();
        Assert.Equal("刷至树脂耗尽 · 浓缩 → 原粹", ResinUsageText.Summary(usage, ResinCapabilities.Domain));
        usage.Mode = ResinUsageMode.RunCount;
        usage.RunCount = 6;
        usage.Allowed.Transient = true;
        Assert.Equal("领奖 6 次 · 浓缩 → 原粹 → 须臾", ResinUsageText.Summary(usage, ResinCapabilities.Domain));
        usage.Mode = ResinUsageMode.PerType;
        usage.Counts = new() { Condensed = 2, Original = 4 };
        Assert.Equal("浓缩 2 次 → 原粹 4 次", ResinUsageText.Summary(usage, ResinCapabilities.Domain));
        usage.OriginalTier = OriginalResinTier.Prefer40;
        Assert.Equal("浓缩 2 次 → 原粹 4 次 · 原粹优先 40", ResinUsageText.Summary(usage, ResinCapabilities.Domain));
        usage.OriginalTier = OriginalResinTier.Only20;
        Assert.Equal("浓缩 2 次 → 原粹 4 次 · 原粹只用 20", ResinUsageText.Summary(usage, ResinCapabilities.Domain));
    }

    /// <summary>无次数、无可用树脂及任务不支持的设置显示对应提示。</summary>
    [Fact]
    public void Summary_AndProblemExplainInvalidSettings()
    {
        Assert.Equal("尚未填写次数", ResinUsageText.Summary(new() { Mode = ResinUsageMode.PerType }, ResinCapabilities.Domain));
        Assert.Equal("尚未填写次数", ResinUsageText.Summary(new() { Mode = ResinUsageMode.RunCount, RunCount = 0 }, ResinCapabilities.Domain));
        Assert.Equal("尚未选择可用树脂", ResinUsageText.Summary(new() { Allowed = new() }, ResinCapabilities.Domain));
        Assert.Null(ResinUsageText.Problem(ResinCapabilities.Boss.Defaults, ResinCapabilities.Boss));
        Assert.Equal("此任务不能使用浓缩树脂。", ResinUsageText.Problem(new(), ResinCapabilities.Boss));
        Assert.Equal("此任务不能使用浓缩树脂。", ResinUsageText.Summary(new(), ResinCapabilities.Boss));
        Assert.Equal("领奖次数须为 1–999。", ResinUsageText.Summary(new() { Mode = ResinUsageMode.RunCount, RunCount = 1000 }, ResinCapabilities.Domain));
    }

    /// <summary>指定次数的达标、树脂不足、没有可用选项三种报告使用设计文案。</summary>
    [Theory]
    [InlineData(6, 160, ResinStopReason.TargetReached, "领奖 6/6 次（浓缩 2、原粹 160）。")]
    [InlineData(4, 80, ResinStopReason.ResinExhausted, "领奖 4/6 次（浓缩 2、原粹 80），树脂不足，提前结束。")]
    [InlineData(4, 80, ResinStopReason.NoUsableOption, "领奖 4/6 次（浓缩 2、原粹 80），未找到可用的树脂选项，提前结束。")]
    public void Report_RunCountUsesExactWording(int claims, int original, ResinStopReason reason, string expected)
    {
        var report = new ResinUsageReport
        {
            Mode = ResinUsageMode.RunCount, ClaimCount = claims, TargetCount = 6,
            Claims = new() { Condensed = 2, Original = claims - 2 },
            Consumed = new() { Condensed = 2, Original = original }, StopReason = reason
        };
        Assert.Equal(expected, ResinUsageText.Report(report));
    }

    /// <summary>耗尽报告不展示目标次数；跳过提示由常量供任务单独使用。</summary>
    [Fact]
    public void Report_ExhaustAndSkippedUseExactWording()
    {
        var report = new ResinUsageReport
        {
            Mode = ResinUsageMode.Exhaust, ClaimCount = 5,
            Claims = new() { Condensed = 2, Original = 3 },
            Consumed = new() { Condensed = 2, Original = 120 }, StopReason = ResinStopReason.ResinExhausted
        };
        Assert.Equal("领奖 5 次（浓缩 2、原粹 120），树脂已用完。", ResinUsageText.Report(report));
        Assert.Equal("树脂不足，未开始领奖。", ResinUsageText.Skipped);
    }

    /// <summary>分别指定使用各类目标及领奖次数，不能将原粹消耗点数当作次数。</summary>
    [Theory]
    [InlineData(ResinStopReason.TargetReached, 4, "浓缩 2/2、原粹 4/4。")]
    [InlineData(ResinStopReason.ResinExhausted, 3, "浓缩 2/2、原粹 3/4，树脂不足，提前结束。")]
    [InlineData(ResinStopReason.NoUsableOption, 3, "浓缩 2/2、原粹 3/4，未找到可用的树脂选项，提前结束。")]
    public void Report_PerTypeUsesPerKindTargets(ResinStopReason reason, int originalClaims, string expected)
    {
        var report = new ResinUsageReport
        {
            Mode = ResinUsageMode.PerType, ClaimCount = originalClaims + 2, TargetCount = 6,
            TargetCounts = new() { Condensed = 2, Original = 4 },
            Claims = new() { Condensed = 2, Original = originalClaims },
            Consumed = new() { Condensed = 2, Original = originalClaims * 40 }, StopReason = reason
        };
        Assert.Equal(expected, ResinUsageText.Report(report));
    }

    /// <summary>首领补充消耗单列为“补充”，直接用于领奖的须臾不标作补充。</summary>
    [Fact]
    public void Report_DistinguishesBossReplenishmentFromDirectUse()
    {
        var report = new ResinUsageReport
        {
            Mode = ResinUsageMode.RunCount, ClaimCount = 6, TargetCount = 6,
            Claims = new() { Original = 6 }, Consumed = new() { Original = 240, Transient = 2 },
            StopReason = ResinStopReason.TargetReached
        };
        Assert.Equal("领奖 6/6 次（原粹 240，补充须臾 2 个）。", ResinUsageText.Report(report));
        report.Consumed.Fragile = 1;
        Assert.Equal("领奖 6/6 次（原粹 240，补充须臾 2 个，补充脆弱 1 个）。", ResinUsageText.Report(report));
        report.Claims.Transient = 2;
        Assert.Equal("领奖 6/6 次（原粹 240、须臾 2，补充脆弱 1 个）。", ResinUsageText.Report(report));
    }

    /// <summary>名称和枚举顺序固定，避免界面与任务各自维护不同文字。</summary>
    [Fact]
    public void DisplayNames_AreConsistent()
    {
        Assert.Equal(new[] { "浓缩树脂", "原粹树脂", "须臾树脂", "脆弱树脂" }, ResinKinds.InOrder.Select(ResinUsageText.DisplayName));
        Assert.Equal(new[] { "浓缩", "原粹", "须臾", "脆弱" }, ResinKinds.InOrder.Select(ResinUsageText.ShortName));
        Assert.Equal(new[] { "默认", "优先 40", "只用 20" }, Enum.GetValues<OriginalResinTier>().Select(ResinUsageText.TierDisplayName));
        Assert.Equal(new[] { "刷至树脂耗尽", "指定领奖次数", "按树脂分别指定" }, Enum.GetValues<ResinUsageMode>().Select(ResinUsageText.ModeDisplayName));
    }

    /// <summary>新增结果容器可按任务合同序列化；未识别合成份数时保留 null。</summary>
    [Fact]
    public void ResultContainers_HaveUsableDefaultsAndSerialize()
    {
        var data = new ResinClaimData();
        Assert.Empty(data.Rewards.Items);
        Assert.Equal(0, data.Resin.ClaimCount);
        var restored = SoloTaskJson.Deserialize<ResinClaimData>(SoloTaskJson.Serialize(data));
        Assert.Empty(restored.Rewards.Items);
        Assert.Equal(new ResinKindValues<int>(), restored.Resin.Consumed);
        var craft = new CraftCondensedResinData();
        Assert.Null(craft.CraftedCount);
        Assert.Contains("\"crafted_count\":null", SoloTaskJson.Serialize(craft));
        craft.CraftedCount = 3;
        Assert.Equal(3, SoloTaskJson.Deserialize<CraftCondensedResinData>(SoloTaskJson.Serialize(craft)).CraftedCount);
    }
}
