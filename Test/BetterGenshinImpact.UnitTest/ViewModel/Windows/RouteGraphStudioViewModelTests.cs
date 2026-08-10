using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.ViewModel.Windows;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest.ViewModel.Windows;

public sealed class RouteGraphStudioViewModelTests
{
    [Fact]
    public void FinishPathDrawing_CreatesPatchNodesAndBidirectionalEdges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-graph-studio-" + Guid.NewGuid().ToString("N"));
        var viewModel = new RouteGraphStudioViewModel(directory, "Teyvat")
        {
            AddBidirectionalEdge = true,
            DrawSnapDistance = 2
        };

        viewModel.StartPathDrawingCommand.Execute(null);
        viewModel.AddDrawPathPointCommand.Execute(new RouteGraphPoint(100, 100));
        viewModel.AddDrawPathPointCommand.Execute(new RouteGraphPoint(110, 100));
        viewModel.FinishPathDrawingCommand.Execute(null);

        Assert.False(viewModel.IsPathDrawing);
        Assert.Empty(viewModel.DraftPathPoints);
        Assert.Equal(2, viewModel.VisibleNodes.Count);
        Assert.Equal(2, viewModel.VisibleEdges.Count);
        Assert.Equal(4, viewModel.PendingOperationCount);
        Assert.All(viewModel.VisibleNodes, node => Assert.Equal("path", node.NodeType));
        Assert.Contains(viewModel.VisibleEdges, edge => edge.FromNodeId == viewModel.VisibleNodes[0].NodeId);
        Assert.Contains(viewModel.VisibleEdges, edge => edge.ToNodeId == viewModel.VisibleNodes[0].NodeId);
    }

    [Fact]
    public void FinishPathDrawing_SimplifiesHeldMouseSamplesWithRdp()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-graph-studio-" + Guid.NewGuid().ToString("N"));
        var viewModel = new RouteGraphStudioViewModel(directory, "Teyvat")
        {
            AddBidirectionalEdge = false,
            DrawSnapDistance = 0
        };

        viewModel.StartPathDrawingCommand.Execute(null);
        for (var x = 100; x <= 200; x++)
        {
            viewModel.AddDrawPathPointCommand.Execute(new RouteGraphPoint(x, 100));
        }
        viewModel.FinishPathDrawingCommand.Execute(null);

        Assert.Equal(2, viewModel.VisibleNodes.Count);
        Assert.Single(viewModel.VisibleEdges);
        Assert.Equal(3, viewModel.PendingOperationCount);
        Assert.Equal(100, viewModel.VisibleNodes[0].X, precision: 2);
        Assert.Equal(200, viewModel.VisibleNodes[1].X, precision: 2);
    }

    [Fact]
    public async Task SavePatch_RoundTripsHandDrawnRoadAndReloadsProvider()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-graph-studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var graph = new RouteNavigationGraph
            {
                GraphId = "roundtrip-graph",
                Nodes =
                [
                    new RouteNavigationNode { NodeId = "a", MapName = "Teyvat", X = 0, Y = 0 },
                    new RouteNavigationNode { NodeId = "b", MapName = "Teyvat", X = 10, Y = 0 }
                ],
                Edges =
                [
                    new RouteNavigationEdge
                    {
                        EdgeId = "base-edge",
                        FromNodeId = "a",
                        ToNodeId = "b",
                        MapName = "Teyvat",
                        Points =
                        [
                            new TelemetryPoint2D { X = 0, Y = 0 },
                            new TelemetryPoint2D { X = 10, Y = 0 }
                        ]
                    }
                ]
            };
            File.WriteAllText(
                Path.Combine(directory, RouteNavigationGraphBuilder.GraphFileName),
                JsonSerializer.Serialize(graph));
            var viewModel = new RouteGraphStudioViewModel(directory, "Teyvat")
            {
                AddBidirectionalEdge = true,
                DrawSnapDistance = 0
            };
            await viewModel.InitializeAsync();
            viewModel.StartPathDrawingCommand.Execute(null);
            viewModel.AddDrawPathPointCommand.Execute(new RouteGraphPoint(100, 100));
            viewModel.AddDrawPathPointCommand.Execute(new RouteGraphPoint(120, 100));
            viewModel.FinishPathDrawingCommand.Execute(null);

            await viewModel.SavePatchCommand.ExecuteAsync(null);

            var patchPath = Assert.Single(Directory.EnumerateFiles(viewModel.OverrideDirectoryPath, "*.json"));
            var patch = JsonSerializer.Deserialize<RouteGraphOverridePatch>(
                File.ReadAllText(patchPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(patch);
            Assert.Equal(4, patch!.Operations.Count);
            Assert.Equal(0, viewModel.PendingOperationCount);
            Assert.DoesNotContain("失败", viewModel.StatusText);
            Assert.DoesNotContain("错误 1", viewModel.StatusText);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FilterMapChange_ClearsConnectionStart()
    {
        var directory = CreateCrossMapGraphDirectory();
        try
        {
            var viewModel = new RouteGraphStudioViewModel(directory, "Teyvat");
            await viewModel.InitializeAsync();
            viewModel.SelectedNode = viewModel.VisibleNodes[0];
            viewModel.SetConnectionStartCommand.Execute(null);

            viewModel.FilterMap = "TheChasm";

            Assert.Null(viewModel.ConnectionStartNode);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AddConnection_DoesNotCreateCrossMapEdge()
    {
        var directory = CreateCrossMapGraphDirectory();
        try
        {
            var viewModel = new RouteGraphStudioViewModel(directory, "Teyvat")
            {
                AddBidirectionalEdge = false
            };
            await viewModel.InitializeAsync();
            var teyvatNode = viewModel.VisibleNodes[0];
            viewModel.FilterMap = "TheChasm";
            viewModel.ConnectionStartNode = teyvatNode;
            viewModel.SelectedNode = viewModel.VisibleNodes[0];

            viewModel.AddConnectionCommand.Execute(null);

            Assert.Equal(0, viewModel.PendingOperationCount);
            Assert.Contains("不同地图", viewModel.StatusText);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ValidatePlan_UsesUnsavedDraftInsteadOfDiskGraph()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-graph-studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var graph = new RouteNavigationGraph
            {
                GraphId = "draft-validation-graph",
                Nodes =
                [
                    new RouteNavigationNode { NodeId = "a", MapName = "Teyvat", X = 0, Y = 0 },
                    new RouteNavigationNode { NodeId = "b", MapName = "Teyvat", X = 1000, Y = 0 }
                ],
                Edges = [CreateEdge("edge", "Teyvat", "a", "b")]
            };
            graph.Edges[0].Points[^1].X = 1000;
            File.WriteAllText(
                Path.Combine(directory, RouteNavigationGraphBuilder.GraphFileName),
                JsonSerializer.Serialize(graph));
            var viewModel = new RouteGraphStudioViewModel(directory, "Teyvat");
            await viewModel.InitializeAsync();
            var start = viewModel.VisibleNodes.Single(node => node.NodeId == "a");
            var target = viewModel.VisibleNodes.Single(node => node.NodeId == "b");
            viewModel.SelectedNode = start;
            viewModel.SetConnectionStartCommand.Execute(null);
            viewModel.SelectedEdge = Assert.Single(viewModel.VisibleEdges);
            viewModel.DisableEdgeCommand.Execute(null);
            viewModel.SelectedNode = target;

            viewModel.ValidatePlanCommand.Execute(null);

            Assert.Contains("规划验证失败", viewModel.StatusText);
            Assert.Equal(1, viewModel.PendingOperationCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateCrossMapGraphDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-graph-studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var graph = new RouteNavigationGraph
        {
            GraphId = "cross-map-graph",
            Nodes =
            [
                new RouteNavigationNode { NodeId = "teyvat-a", MapName = "Teyvat", X = 0, Y = 0 },
                new RouteNavigationNode { NodeId = "teyvat-b", MapName = "Teyvat", X = 10, Y = 0 },
                new RouteNavigationNode { NodeId = "chasm-a", MapName = "TheChasm", X = 0, Y = 0 },
                new RouteNavigationNode { NodeId = "chasm-b", MapName = "TheChasm", X = 10, Y = 0 }
            ],
            Edges =
            [
                CreateEdge("teyvat-edge", "Teyvat", "teyvat-a", "teyvat-b"),
                CreateEdge("chasm-edge", "TheChasm", "chasm-a", "chasm-b")
            ]
        };
        File.WriteAllText(
            Path.Combine(directory, RouteNavigationGraphBuilder.GraphFileName),
            JsonSerializer.Serialize(graph));
        return directory;
    }

    private static RouteNavigationEdge CreateEdge(
        string edgeId,
        string mapName,
        string fromNodeId,
        string toNodeId) =>
        new()
        {
            EdgeId = edgeId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            MapName = mapName,
            Points =
            [
                new TelemetryPoint2D { X = 0, Y = 0 },
                new TelemetryPoint2D { X = 10, Y = 0 }
            ]
        };
}
