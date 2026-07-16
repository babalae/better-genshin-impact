using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public class RouteNavigationPlannerTests
{
    public RouteNavigationPlannerTests()
    {
        TestConfigEnvironment.EnsureInitialized();
    }

    [Theory]
    [InlineData(RouteNavigationGraphLoadStatus.FileMissing, RouteNavigationFailureCode.GraphFileMissing)]
    [InlineData(RouteNavigationGraphLoadStatus.Empty, RouteNavigationFailureCode.GraphEmpty)]
    [InlineData(RouteNavigationGraphLoadStatus.Invalid, RouteNavigationFailureCode.GraphInvalid)]
    public void TryPlan_ReportsStableGraphLoadFailure(RouteNavigationGraphLoadStatus loadStatus, RouteNavigationFailureCode expected)
    {
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(RouteNavigationGraphSnapshot.Empty, loadStatus), new FakeCoordinateConverter());

        var succeeded = planner.TryPlan(CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(10, 0)), out var plan, StrictOptions());

        Assert.False(succeeded);
        Assert.Equal(expected, plan.FailureCode);
    }

    [Fact]
    public void TryPlan_ReportsCurrentPointCannotAttach()
    {
        var snapshot = CreateSnapshot((0, 0), (10, 0));
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new FakeCoordinateConverter());

        var succeeded = planner.TryPlan(CreateRequest(new RouteGraphPoint(100, 100), new RouteGraphPoint(10, 0)), out var plan, StrictOptions());

        Assert.False(succeeded);
        Assert.Equal(RouteNavigationFailureCode.CurrentPointNotConnected, plan.FailureCode);
    }

    [Fact]
    public void TryPlan_ReportsTargetPointCannotAttach()
    {
        var snapshot = CreateSnapshot((0, 0), (10, 0));
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new FakeCoordinateConverter());

        var succeeded = planner.TryPlan(CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(100, 100)), out var plan, StrictOptions());

        Assert.False(succeeded);
        Assert.Equal(RouteNavigationFailureCode.TargetPointNotConnected, plan.FailureCode);
    }

    [Fact]
    public void TryPlan_UsesCoordinateConverterForPathExecutorWaypoints()
    {
        var converter = new FakeCoordinateConverter();
        var snapshot = CreateSnapshot((0, 0), (10, 0));
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), converter);

        var succeeded = planner.TryPlan(CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(10, 0)), out var plan, StrictOptions());

        Assert.True(succeeded);
        Assert.NotNull(plan.Task);
        Assert.True(converter.ImageToGameCallCount >= 2);
        Assert.All(plan.Task!.Positions, waypoint => Assert.True(waypoint.X >= 1000));
        Assert.Equal(WaypointType.Target.Code, plan.Task.Positions[^1].Type);
        Assert.NotNull(plan.Request);
        Assert.NotNull(plan.Options);
    }

    [Fact]
    public void TryPlan_TargetCoordinateConversionFailureFailsThePlan()
    {
        var converter = new FakeCoordinateConverter(failAtImageX: 10);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(CreateSnapshot((0, 0), (10, 0))), converter);

        var succeeded = planner.TryPlan(CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(10, 0)), out var plan, StrictOptions());

        Assert.False(succeeded);
        Assert.Equal(RouteNavigationFailureCode.CoordinateConversionFailed, plan.FailureCode);
    }

    [Fact]
    public void TryPlan_NearTargetStillUsesTheExactTargetCoordinate()
    {
        var converter = new FakeCoordinateConverter();
        var planner = new RouteNavigationPlanner(
            new FakeGraphProvider(CreateSnapshot((0, 0), (10, 0))),
            converter);
        var options = StrictOptions(targetOutputMinDistance: 2);

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(10.5, 0)),
            out var plan,
            options);

        Assert.True(succeeded);
        Assert.NotNull(plan.Task);
        Assert.Equal(1010.5, plan.Task!.Positions[^1].X, precision: 2);
        Assert.Equal(2000, plan.Task.Positions[^1].Y, precision: 2);
        Assert.Equal(WaypointType.Target.Code, plan.Task.Positions[^1].Type);
    }

    private static RouteNavigationGraphSnapshot CreateSnapshot((double X, double Y) from, (double X, double Y) to)
    {
        var fromNode = new RouteNavigationNode { NodeId = "from", MapName = "Teyvat", X = (float)from.X, Y = (float)from.Y };
        var toNode = new RouteNavigationNode { NodeId = "to", MapName = "Teyvat", X = (float)to.X, Y = (float)to.Y };
        var graph = new RouteNavigationGraph
        {
            Nodes = [fromNode, toNode],
            Edges =
            [
                new RouteNavigationEdge
                {
                    EdgeId = "edge",
                    FromNodeId = fromNode.NodeId,
                    ToNodeId = toNode.NodeId,
                    MapName = "Teyvat",
                    MoveMode = MoveModeEnum.Walk.Code,
                    Cost = 10,
                    Points =
                    [
                        new TelemetryPoint2D { X = (float)from.X, Y = (float)from.Y },
                        new TelemetryPoint2D { X = (float)to.X, Y = (float)to.Y }
                    ]
                }
            ]
        };
        return new RouteNavigationGraphSnapshot(graph, 64, []);
    }

    private static RouteNavigationPlanRequest CreateRequest(RouteGraphPoint current, RouteGraphPoint target)
    {
        return new RouteNavigationPlanRequest
        {
            MapName = "Teyvat",
            MapMatchMethod = "TemplateMatch",
            CurrentImagePoint = current,
            TargetImagePoint = target,
            TaskName = "test"
        };
    }

    private static RouteNavigationPlanOptions StrictOptions(double targetOutputMinDistance = 0)
    {
        return new RouteNavigationPlanOptions
        {
            AllowTeleport = false,
            AllowUnknownStartConnector = false,
            AllowUnknownTargetConnector = false,
            CurrentAttachMaxDistance = 2,
            TargetAttachMaxDistance = 2,
            OutputPointMinDistance = 0,
            TargetOutputMinDistance = targetOutputMinDistance
        };
    }

    private sealed class FakeGraphProvider(
        RouteNavigationGraphSnapshot snapshot,
        RouteNavigationGraphLoadStatus loadStatus = RouteNavigationGraphLoadStatus.Loaded) : IRouteNavigationGraphProvider
    {
        public string GraphFilePath => "fake.json";

        public bool TryGetSnapshot(out RouteNavigationGraphSnapshot result, out RouteNavigationGraphLoadStatus status, bool forceReload = false)
        {
            result = snapshot;
            status = loadStatus;
            return loadStatus == RouteNavigationGraphLoadStatus.Loaded && !snapshot.IsEmpty;
        }
    }

    private sealed class FakeCoordinateConverter(double? failAtImageX = null) : IRouteCoordinateConverter
    {
        public int ImageToGameCallCount { get; private set; }

        public bool TryImageToGame(string mapName, string? mapMatchMethod, RouteGraphPoint imagePoint, out RouteGamePoint gamePoint)
        {
            ImageToGameCallCount++;
            if (failAtImageX.HasValue && Math.Abs(imagePoint.X - failAtImageX.Value) < 0.001)
            {
                gamePoint = default;
                return false;
            }

            gamePoint = new RouteGamePoint(1000 + imagePoint.X, 2000 + imagePoint.Y);
            return true;
        }

        public bool TryGameToImage(string mapName, string? mapMatchMethod, RouteGamePoint gamePoint, out RouteGraphPoint imagePoint)
        {
            imagePoint = new RouteGraphPoint(gamePoint.X - 1000, gamePoint.Y - 2000);
            return true;
        }
    }
}
