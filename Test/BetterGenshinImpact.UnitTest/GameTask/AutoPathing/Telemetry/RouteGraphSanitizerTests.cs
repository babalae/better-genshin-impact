using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public class RouteGraphSanitizerTests
{
    [Fact]
    public void RemoveImpossibleImportedEdges_RemovesOnlyUnverifiedLongHistoricalLines()
    {
        var from = Node("from", 0);
        var longEnd = Node("long", 1000); // Teyvat 中约 500 游戏单位。
        var verifiedEnd = Node("verified", 1200);
        var telemetryEnd = Node("telemetry", 1400);
        var graph = new RouteNavigationGraph
        {
            Nodes = [from, longEnd, verifiedEnd, telemetryEnd],
            Edges =
            [
                Edge("remove", from, longEnd, "pathing_task", GraphReviewStatus.Unreviewed),
                Edge("keep-verified", from, verifiedEnd, "pathing_task", GraphReviewStatus.Verified),
                Edge("keep-telemetry", from, telemetryEnd, "telemetry", GraphReviewStatus.Unreviewed)
            ]
        };

        var removed = RouteGraphSanitizer.RemoveImpossibleImportedEdges(graph);

        Assert.Equal(1, removed);
        Assert.DoesNotContain(graph.Edges, edge => edge.EdgeId == "remove");
        Assert.Contains(graph.Edges, edge => edge.EdgeId == "keep-verified");
        Assert.Contains(graph.Edges, edge => edge.EdgeId == "keep-telemetry");
    }

    private static RouteNavigationNode Node(string id, double x) => new()
    {
        NodeId = id,
        MapName = "Teyvat",
        X = x,
        Y = 0
    };

    private static RouteNavigationEdge Edge(
        string id,
        RouteNavigationNode from,
        RouteNavigationNode to,
        string sourceKind,
        GraphReviewStatus reviewStatus) => new()
    {
        EdgeId = id,
        FromNodeId = from.NodeId,
        ToNodeId = to.NodeId,
        MapName = "Teyvat",
        SourceKind = sourceKind,
        ReviewStatus = reviewStatus,
        Points =
        [
            new TelemetryPoint2D { X = (float)from.X, Y = (float)from.Y },
            new TelemetryPoint2D { X = (float)to.X, Y = (float)to.Y }
        ]
    };
}
