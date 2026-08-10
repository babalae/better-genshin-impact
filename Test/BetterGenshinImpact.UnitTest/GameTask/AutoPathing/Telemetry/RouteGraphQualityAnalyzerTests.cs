using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public class RouteGraphQualityAnalyzerTests
{
    [Fact]
    public void Analyze_FindsInvalidTopologyAndRiskySyntheticReverse()
    {
        var a = new RouteNavigationNode { NodeId = "a", MapName = "Teyvat", X = 0, Y = 0 };
        var b = new RouteNavigationNode { NodeId = "b", MapName = "Teyvat", X = 500, Y = 0, LayerId = "underground" };
        var graph = new RouteNavigationGraph
        {
            Nodes = [a, b],
            Edges =
            [
                new RouteNavigationEdge
                {
                    EdgeId = "long-reverse",
                    FromNodeId = "a",
                    ToNodeId = "b",
                    MapName = "Teyvat",
                    IsSyntheticReverse = true,
                    ReviewStatus = GraphReviewStatus.Risky
                },
                new RouteNavigationEdge { EdgeId = "self", FromNodeId = "a", ToNodeId = "a", MapName = "Teyvat" }
            ]
        };

        var issues = new RouteGraphQualityAnalyzer().Analyze(graph);

        Assert.Contains(issues, issue => issue.Code == RouteGraphQualityIssueCode.ExcessiveStraightEdge);
        Assert.Contains(issues, issue => issue.Code == RouteGraphQualityIssueCode.CrossLayerEdge);
        Assert.Contains(issues, issue => issue.Code == RouteGraphQualityIssueCode.SyntheticReverseNeedsReview);
        Assert.Contains(issues, issue => issue.Code == RouteGraphQualityIssueCode.SelfLoop);
    }

    [Fact]
    public void Analyze_FindsMissingAndMisalignedTeleportEntries()
    {
        var graph = new RouteNavigationGraph
        {
            Nodes =
            [
                new RouteNavigationNode
                {
                    NodeId = "entry",
                    MapName = "Teyvat",
                    X = 100,
                    Y = 0,
                    AnchorIds = ["far-entry"]
                }
            ]
        };
        var teleports = new List<RouteGraphTeleportEntry>
        {
            CreateTeleport("missing", "missing", 0),
            CreateTeleport("far-entry", "far", 0)
        };

        var issues = new RouteGraphQualityAnalyzer().Analyze(graph, teleports);

        Assert.Contains(issues, issue => issue.Code == RouteGraphQualityIssueCode.TeleportWithoutEntry);
        Assert.Contains(issues, issue => issue.Code == RouteGraphQualityIssueCode.TeleportEntryTooFar);
    }

    private static RouteGraphTeleportEntry CreateTeleport(string anchorId, string name, double x)
    {
        return new RouteGraphTeleportEntry(
            "Teyvat", anchorId, anchorId, name, "TeleportWaypoint",
            x, 0, x, 0, x, 0, x, 0);
    }
}
