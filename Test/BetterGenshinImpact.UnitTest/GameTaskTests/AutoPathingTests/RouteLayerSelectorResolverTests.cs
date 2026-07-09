using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathingTests;

public class RouteLayerSelectorResolverTests
{
    public RouteLayerSelectorResolverTests()
    {
        _ = new ConfigService().Get();
    }

    [Fact]
    public void ResolveEffectiveSelector_ShouldInheritRouteLevelSelector_WhenNoWaypointOrSegmentOverrideExists()
    {
        var info = new PathingTaskInfo
        {
            MapLayerGroupId = "33403",
            MapLayerMode = MapLayerSelector.ModeRequire
        };
        var waypoint = new Waypoint { Id = 10 };

        var selector = RouteLayerSelectorResolver.ResolveEffectiveSelector(info, waypoint, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.False(selector.IsEmpty);
        Assert.True(selector.IsRequire);
        Assert.Equal("33403", selector.MapLayerGroupId);
    }

    [Fact]
    public void ResolveEffectiveSelector_ShouldSuppressRouteLevelSelector_WhenWaypointModeIsExplicitlyUnspecified()
    {
        var info = new PathingTaskInfo
        {
            MapLayerGroupId = "33403",
            MapLayerMode = MapLayerSelector.ModeRequire
        };
        var waypoint = new Waypoint
        {
            Id = 10,
            MapLayerMode = MapLayerSelector.ModeUnspecified
        };

        var selector = RouteLayerSelectorResolver.ResolveEffectiveSelector(info, waypoint, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.True(selector.IsEmpty);
        Assert.Equal("legacy", selector.StateKey);
        Assert.Null(selector.MapLayerGroupId);
    }

    [Fact]
    public void ResolveEffectiveSelector_ShouldMatchSegmentsByWaypointId_AndNotFallbackToOrdinalWhenIdIsMissing()
    {
        var info = new PathingTaskInfo
        {
            MapLayerGroupId = "route-group",
            MapLayerMode = MapLayerSelector.ModeRequire,
            MapLayerSegments =
            [
                new MapLayerSegment
                {
                    FromId = 20,
                    ToId = 30,
                    MapLayerGroupId = "segment-group",
                    MapLayerMode = MapLayerSelector.ModeRequire
                }
            ]
        };

        var segmentSelector = RouteLayerSelectorResolver.ResolveEffectiveSelector(
            info,
            new Waypoint { Id = 25 },
            out var segmentDiagnostics);
        var missingIdSelector = RouteLayerSelectorResolver.ResolveEffectiveSelector(
            info,
            new Waypoint(),
            out var missingIdDiagnostics);

        Assert.Empty(segmentDiagnostics);
        Assert.Equal("segment-group", segmentSelector.MapLayerGroupId);
        Assert.True(segmentSelector.IsRequire);

        Assert.Equal("route-group", missingIdSelector.MapLayerGroupId);
        Assert.True(missingIdSelector.IsRequire);
        Assert.Contains("Waypoint has no id; map_layer_segments cannot match by ordinal.", missingIdDiagnostics);
    }

    [Fact]
    public void ValidateTask_ShouldRejectRoute523_WhenRouteLevelSelectorHasNoWaypointOrSegmentOverride()
    {
        var task = new PathingTask
        {
            Info = new PathingTaskInfo
            {
                Name = "523枫丹很明亮的地方（400_1）",
                MapLayerGroupId = "33403",
                MapLayerMode = MapLayerSelector.ModeRequire
            },
            Positions = [new Waypoint { Id = 1 }]
        };

        var diagnostics = RouteLayerSelectorResolver.ValidateTask(task);

        Assert.Contains("Route 523 must not use route-level-only map layer metadata.", diagnostics);
    }
}
