using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathingTests;

public class NavigationInstanceTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(33096.273f, 12270.404f, true)]
    [InlineData(13595, 20102, false)]
    public void ShouldUseTemplateMatchFallback_ShouldRejectMissingOrDistantSiftMatches(
        float matchedX,
        float matchedY,
        bool expected)
    {
        var matchedPosition = new Point2f(matchedX, matchedY);
        var expectedPosition = new Point2f(13587.977f, 20106.629f);

        var actual = NavigationInstance.ShouldUseTemplateMatchFallback(
            primaryUsesTemplateMatch: false,
            matchedPosition,
            expectedPosition,
            MapLayerSelector.Empty);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShouldUseTemplateMatchFallback_ShouldHonorExplicitLayerSelectorWithoutRecursing()
    {
        var selector = new MapLayerSelector
        {
            MapLayerGroupId = "33403",
            MapLayerMode = MapLayerSelector.ModeRequire
        };
        var position = new Point2f(13595, 20102);

        Assert.True(NavigationInstance.ShouldUseTemplateMatchFallback(
            primaryUsesTemplateMatch: false,
            position,
            position,
            selector));
        Assert.False(NavigationInstance.ShouldUseTemplateMatchFallback(
            primaryUsesTemplateMatch: true,
            position,
            position,
            selector));
    }
}
