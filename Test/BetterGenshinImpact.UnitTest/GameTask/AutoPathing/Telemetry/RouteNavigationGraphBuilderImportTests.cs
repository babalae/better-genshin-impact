using System.Text.Json;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public sealed class RouteNavigationGraphBuilderImportTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "BetterGI.RouteNavigationGraphBuilderImportTests",
        Guid.NewGuid().ToString("N"));

    public RouteNavigationGraphBuilderImportTests()
    {
        TestConfigEnvironment.EnsureInitialized();
    }

    [Fact]
    public void BuildNow_SnapsNearbyImportedNodesAndWritesBidirectionalGraph()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "output")).FullName;
        WriteRoute(sourceDirectory, "first.json", 0, 0, 10, 0);
        WriteRoute(sourceDirectory, "second.json", 10.2, 0, 20, 0);
        var builder = new RouteNavigationGraphBuilder(outputDirectory, new IdentityCoordinateConverter());

        var result = builder.BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = false,
            PathingTaskDirectories = [sourceDirectory],
            NodeSnapDistance = 6
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, result.Graph.Nodes.Count);
        Assert.Equal(4, result.Graph.Edges.Count);
        Assert.Equal(2, result.Graph.SchemaVersion);
        Assert.All(result.Graph.Edges, edge => Assert.Equal("pathing_task", edge.SourceKind));
        Assert.Equal(
            ["first.json", "second.json"],
            result.Graph.Edges.Select(edge => edge.SourceFileName).Distinct().Order().ToArray());
        Assert.All(result.Graph.Edges, edge => Assert.StartsWith("path_route_", edge.SourceRecordId));
        Assert.True(File.Exists(result.OutputPath));

        var json = File.ReadAllText(result.OutputPath);
        var persisted = JsonSerializer.Deserialize<RouteNavigationGraph>(json);
        Assert.NotNull(persisted);
        Assert.Equal(3, persisted!.Nodes.Count);
        Assert.Equal(4, persisted.Edges.Count);
    }

    [Fact]
    public void BuildNow_MergesEdgesThatBecomeEqualAfterNodeSnapping()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "dedupe-source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "dedupe-output")).FullName;
        WriteRoute(sourceDirectory, "first.json", 0, 0, 10, 0);
        WriteRoute(sourceDirectory, "second.json", 0.2, 0, 10.2, 0);
        var builder = new RouteNavigationGraphBuilder(outputDirectory, new IdentityCoordinateConverter());

        var result = builder.BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = false,
            PathingTaskDirectories = [sourceDirectory],
            NodeSnapDistance = 6
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.Graph.Nodes.Count);
        Assert.Equal(2, result.Graph.Edges.Count);
        Assert.All(result.Graph.Edges, edge => Assert.Equal(2, edge.SourceCount));
    }

    [Fact]
    public void BuildNow_WhenNoUsableEdgesExist_DoesNotOverwriteExistingGraph()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "empty-source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "protected-output")).FullName;
        File.WriteAllText(Path.Combine(sourceDirectory, "single-point.json"), """
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 1, "y": 2, "type": "target", "move_mode": "walk" }
              ]
            }
            """);
        var graphPath = Path.Combine(outputDirectory, RouteNavigationGraphBuilder.GraphFileName);
        const string existingGraph = "existing-graph-must-survive";
        File.WriteAllText(graphPath, existingGraph);
        var builder = new RouteNavigationGraphBuilder(outputDirectory, new IdentityCoordinateConverter());

        var result = builder.BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = false,
            PathingTaskDirectories = [sourceDirectory],
            NodeSnapDistance = 6
        });

        Assert.False(result.Success);
        Assert.Contains("可用", result.ErrorMessage);
        Assert.Equal(existingGraph, File.ReadAllText(graphPath));
    }

    [Fact]
    public void FormatSummary_IncludesGraphAndImportCounts()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "summary-source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "summary-output")).FullName;
        WriteRoute(sourceDirectory, "route.json", 0, 0, 10, 0);
        File.WriteAllText(Path.Combine(sourceDirectory, "not-route.json"), "{ \"name\": \"metadata\" }");
        var builder = new RouteNavigationGraphBuilder(outputDirectory, new IdentityCoordinateConverter());
        var result = builder.BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = false,
            PathingTaskDirectories = [sourceDirectory]
        });

        var summary = RouteNavigationBuildSummaryFormatter.Format(result);

        Assert.Contains("2 个节点", summary);
        Assert.Contains("2 条边", summary);
        Assert.Contains("扫描 2 个 JSON", summary);
        Assert.Contains("识别 1 条路线", summary);
        Assert.Contains("跳过 1 个", summary);
        Assert.Contains(result.OutputPath, summary);
    }

    [Fact]
    public void ImportedGraph_CanBeLoadedAndPlannedIntoPathingTask()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "planner-source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "planner-output")).FullName;
        WriteRoute(sourceDirectory, "first.json", 0, 0, 10, 0);
        WriteRoute(sourceDirectory, "second.json", 10.2, 0, 20, 0);
        var converter = new IdentityCoordinateConverter();
        var buildResult = new RouteNavigationGraphBuilder(outputDirectory, converter).BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = false,
            PathingTaskDirectories = [sourceDirectory],
            NodeSnapDistance = 6
        });
        Assert.True(buildResult.Success, buildResult.ErrorMessage);

        var provider = new RouteNavigationGraphProvider(outputDirectory);
        Assert.True(provider.TryGetSnapshot(out var snapshot, out var loadStatus, forceReload: true));
        Assert.Equal(RouteNavigationGraphLoadStatus.Loaded, loadStatus);
        Assert.False(snapshot.IsEmpty);
        var planner = new RouteNavigationPlanner(provider, converter);

        var planned = planner.TryPlan(
            new RouteNavigationPlanRequest
            {
                MapName = "Teyvat",
                MapMatchMethod = "TemplateMatch",
                CurrentImagePoint = new RouteGraphPoint(0, 0),
                TargetImagePoint = new RouteGraphPoint(20, 0),
                TaskName = "imported route"
            },
            out var plan,
            new RouteNavigationPlanOptions
            {
                AllowTeleport = false,
                AllowUnknownStartConnector = false,
                AllowUnknownTargetConnector = false,
                CurrentAttachMaxDistance = 2,
                TargetAttachMaxDistance = 2,
                OutputPointMinDistance = 0,
                TargetOutputMinDistance = 0
            });

        Assert.True(planned, plan.FailureReason);
        Assert.NotNull(plan.Task);
        Assert.True(plan.Task!.Positions.Count >= 2);
        Assert.Equal(20, plan.Task.Positions[^1].X, precision: 2);
        Assert.Equal(0, plan.Task.Positions[^1].Y, precision: 2);
    }

    [Fact]
    public void BuildNow_WhenGraphReplacementFails_CleansTemporaryFile()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "write-failure-source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "write-failure-output")).FullName;
        WriteRoute(sourceDirectory, "route.json", 0, 0, 10, 0);
        var graphPath = Path.Combine(outputDirectory, RouteNavigationGraphBuilder.GraphFileName);
        Directory.CreateDirectory(graphPath);
        var builder = new RouteNavigationGraphBuilder(outputDirectory, new IdentityCoordinateConverter());

        var result = builder.BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = false,
            PathingTaskDirectories = [sourceDirectory]
        });

        Assert.False(result.Success);
        Assert.False(File.Exists(graphPath + ".tmp"));
    }

    [Fact]
    public void BuildNow_WhenExplicitImportProducesNoEdges_DoesNotSucceedFromTelemetryAlone()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "no-import-edges-source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "no-import-edges-output")).FullName;
        File.WriteAllText(Path.Combine(sourceDirectory, "single-point.json"), """
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 1, "y": 2, "type": "target", "move_mode": "walk" }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(outputDirectory, "seed_Telemetry.json"),
            JsonSerializer.Serialize(new[]
            {
                new RouteTelemetryRecord
                {
                    RecordId = "record",
                    SegmentId = "telemetry-segment",
                    MapName = "Teyvat",
                    SegmentKey = "0,0->10,0",
                    MoveMode = "walk",
                    Points =
                    [
                        new TelemetryPoint2D { X = 0, Y = 0 },
                        new TelemetryPoint2D { X = 10, Y = 0 }
                    ]
                }
            }));
        var graphPath = Path.Combine(outputDirectory, RouteNavigationGraphBuilder.GraphFileName);
        const string existingGraph = "existing-imported-graph";
        File.WriteAllText(graphPath, existingGraph);
        var builder = new RouteNavigationGraphBuilder(outputDirectory, new IdentityCoordinateConverter());

        var result = builder.BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = true,
            PathingTaskDirectories = [sourceDirectory]
        });

        Assert.False(result.Success);
        Assert.Contains("所选", result.ErrorMessage);
        Assert.Equal(existingGraph, File.ReadAllText(graphPath));
    }

    [Fact]
    public void BuildNow_AssignsDistinctEdgeIdsToDifferentSafeActionParameters()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "safe-param-ids-source")).FullName;
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "safe-param-ids-output")).FullName;
        WriteSafeActionRoute(sourceDirectory, "first.json", "first-params");
        WriteSafeActionRoute(sourceDirectory, "second.json", "second-params");
        var builder = new RouteNavigationGraphBuilder(outputDirectory, new IdentityCoordinateConverter());

        var result = builder.BuildNow(new RouteNavigationBuildRequest
        {
            IncludeTelemetry = false,
            PathingTaskDirectories = [sourceDirectory]
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.Graph.Edges.Count);
        Assert.Equal(2, result.Graph.Edges.Select(edge => edge.EdgeId).Distinct().Count());
        Assert.Equal(2, result.Graph.Edges.Select(edge => edge.ActionParams).Distinct().Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void WriteRoute(
        string directory,
        string fileName,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        File.WriteAllText(Path.Combine(directory, fileName), $$"""
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": {{startX}}, "y": {{startY}}, "type": "path", "move_mode": "walk" },
                { "x": {{endX}}, "y": {{endY}}, "type": "target", "move_mode": "walk" }
              ]
            }
            """);
    }

    private static void WriteSafeActionRoute(string directory, string fileName, string actionParams)
    {
        File.WriteAllText(Path.Combine(directory, fileName), $$"""
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "target", "move_mode": "walk",
                  "action": "up_down_grab_leaf", "action_params": "{{actionParams}}" }
              ]
            }
            """);
    }

    private sealed class IdentityCoordinateConverter : IRouteCoordinateConverter
    {
        public bool TryImageToGame(
            string mapName,
            string? mapMatchMethod,
            RouteGraphPoint imagePoint,
            out RouteGamePoint gamePoint)
        {
            gamePoint = new RouteGamePoint(imagePoint.X, imagePoint.Y);
            return true;
        }

        public bool TryGameToImage(
            string mapName,
            string? mapMatchMethod,
            RouteGamePoint gamePoint,
            out RouteGraphPoint imagePoint)
        {
            imagePoint = new RouteGraphPoint(gamePoint.X, gamePoint.Y);
            return true;
        }
    }
}
