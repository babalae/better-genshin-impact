using BetterGenshinImpact.GameTask.AutoPick;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPickTests;

public class AutoPickTriggerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("şÉ")]
    public void ShouldFallbackToPaddleOcr_WhenYapTextCleansToNoUsablePickName(string rawText)
    {
        Assert.True(AutoPickTrigger.ShouldFallbackToPaddleOcr(rawText));
    }

    [Theory]
    [InlineData("薄荷")]
    [InlineData("调查")]
    [InlineData("「薄荷」")]
    public void ShouldNotFallbackToPaddleOcr_WhenYapTextContainsUsableChineseName(string rawText)
    {
        Assert.False(AutoPickTrigger.ShouldFallbackToPaddleOcr(rawText));
    }

    [Theory]
    [InlineData("调查", "BlackList")]
    [InlineData("前往声望奖励", "FuzzyBlackList")]
    [InlineData("月谕圣牌", "DoNotPick")]
    public void PickListDecisionTriggersControllerBackoff_WhenBlacklisted(string rawText, string expectedDecisionName)
    {
        var expectedDecision = Enum.Parse<AutoPickTrigger.PickListDecision>(expectedDecisionName);
        var config = new AutoPickConfig
        {
            Mode = AutoPickMode.Blacklist
        };

        var decision = AutoPickTrigger.EvaluatePickLists(
            rawText,
            isExcludeIcon: false,
            config,
            new HashSet<string> { "调查" },
            ["声望"],
            new HashSet<string>(),
            out _);

        Assert.Equal(expectedDecision, decision);
        Assert.True(AutoPickTrigger.ShouldBackOffControllerYForPickListDecision(decision));
    }

    [Fact]
    public void PickListDecisionTriggersControllerBackoff_WhenPromptIconIsExcluded()
    {
        var config = new AutoPickConfig
        {
            Mode = AutoPickMode.Blacklist
        };

        var decision = AutoPickTrigger.EvaluatePickLists(
            "凯瑟琳",
            isExcludeIcon: true,
            config,
            new HashSet<string>(),
            [],
            new HashSet<string>(),
            out _);

        Assert.Equal(AutoPickTrigger.PickListDecision.ExcludeIcon, decision);
        Assert.True(AutoPickTrigger.ShouldBackOffControllerYForPickListDecision(decision));
    }

    [Fact]
    public void PickListDecisionBacksOff_WhenWhitelistPromptIconIsExcluded()
    {
        var config = new AutoPickConfig
        {
            Mode = AutoPickMode.Whitelist
        };

        var decision = AutoPickTrigger.EvaluatePickLists(
            "凯瑟琳",
            isExcludeIcon: true,
            config,
            new HashSet<string> { "凯瑟琳" },
            [],
            new HashSet<string> { "凯瑟琳" },
            out var normalizedText);

        Assert.Equal("凯瑟琳", normalizedText);
        Assert.Equal(AutoPickTrigger.PickListDecision.ExcludeIcon, decision);
        Assert.True(AutoPickTrigger.ShouldBackOffControllerYForPickListDecision(decision));
    }

    [Fact]
    public void PickListDecisionAllowsConfiguredBlacklistModePickBeforeIconExclusion()
    {
        var config = new AutoPickConfig
        {
            Mode = AutoPickMode.Blacklist,
            BlacklistModePickEnabled = true
        };

        var decision = AutoPickTrigger.EvaluatePickLists(
            "凯瑟琳",
            isExcludeIcon: true,
            config,
            new HashSet<string>(),
            [],
            new HashSet<string> { "凯瑟琳" },
            out _);

        Assert.Equal(AutoPickTrigger.PickListDecision.Allow, decision);
        Assert.False(AutoPickTrigger.ShouldBackOffControllerYForPickListDecision(decision));
    }

    [Theory]
    [InlineData("", "EmptyText")]
    [InlineData("A", "TooShort")]
    public void PickListDecisionDoesNotBackoff_WhenOcrTextIsNotUsable(string rawText, string expectedDecisionName)
    {
        var expectedDecision = Enum.Parse<AutoPickTrigger.PickListDecision>(expectedDecisionName);
        var decision = AutoPickTrigger.EvaluatePickLists(
            rawText,
            isExcludeIcon: false,
            new AutoPickConfig(),
            new HashSet<string>(),
            [],
            new HashSet<string>(),
            out _);

        Assert.Equal(expectedDecision, decision);
        Assert.False(AutoPickTrigger.ShouldBackOffControllerYForPickListDecision(decision));
    }
}
