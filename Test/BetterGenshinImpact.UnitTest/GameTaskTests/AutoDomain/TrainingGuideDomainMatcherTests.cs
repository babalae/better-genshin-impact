using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideDomainMatcherTests
{
    private static TrainingGuideDomainMatcher CreateMatcher() => new(
        new[] { "荒坠的圣迹", "逆悬的冰河", "赝月的研究所" },
        text => string.Concat(text.Where(c => !char.IsWhiteSpace(c))));

    [Theory]
    [InlineData("荒坠的圣迹", true)]
    [InlineData("逆悬的冰河", false)]
    [InlineData("赝月的研究所", false)]
    public void KnownTargets_DistinguishMaterialsFromUnsupportedDomains(string name, bool supported)
    {
        var result = CreateMatcher().Match(name);
        Assert.NotNull(result);
        Assert.Equal(name, result.Value.Name);
        Assert.Equal(supported, result.Value.Supported);
    }

    [Theory]
    [InlineData("")]
    [InlineData("征讨领域")]
    [InlineData("赝月的研宄所")]
    [InlineData("未知秘境")]
    [InlineData("荒坠的圣迹 赝月的研究所")]
    public void UnknownOrAmbiguousText_DoesNotCountAsRecognizedTarget(string text)
    {
        Assert.Null(CreateMatcher().Match(text));
    }

    [Fact]
    public void WeeklyOnlyList_IsRecognizedButProducesNoPlanningCandidates()
    {
        var matches = new[] { "征讨领域", "赝月的研究所" }
            .Select(CreateMatcher().Match).Where(result => result.HasValue).Select(result => result!.Value).ToArray();
        Assert.NotEmpty(matches);
        Assert.Empty(matches.Where(result => result.Supported));
    }

    [Fact]
    public void MixedList_OnlyKeepsMaterialDomain()
    {
        var matcher = CreateMatcher();
        var candidates = new[] { "逆悬的冰河", "荒坠 的圣迹", "赝月的研究所" }
            .Select(matcher.Match).Where(result => result is { Supported: true })
            .Select(result => result!.Value.Name).ToArray();
        Assert.Equal(new[] { "荒坠的圣迹" }, candidates);
    }
}
