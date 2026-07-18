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
    public void TryPlan_DoesNotUseCurrentLocalFallbackWhenTargetIsFar()
    {
        var snapshot = CreateSnapshot((0, 0), (10, 0));
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new FakeCoordinateConverter());

        var succeeded = planner.TryPlan(CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(100, 100)), out var plan, StrictOptions());

        Assert.False(succeeded);
        Assert.Equal(RouteNavigationFailureCode.NoRoute, plan.FailureCode);
    }

    [Fact]
    public void TryPlan_ReturnsPartialPlanWhenTargetComponentIsDisconnected()
    {
        var current = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 };
        var frontier = new RouteNavigationNode { NodeId = "frontier", MapName = "Teyvat", X = 40, Y = 0 };
        var target = new RouteNavigationNode { NodeId = "target", MapName = "Teyvat", X = 100, Y = 0 };
        var targetTail = new RouteNavigationNode { NodeId = "target-tail", MapName = "Teyvat", X = 110, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [current, frontier, target, targetTail],
                Edges =
                [
                    CreateHistoricalEdge("reachable", current, frontier),
                    CreateHistoricalEdge("disconnected", target, targetTail)
                ]
            },
            64,
            []);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(100, 0)),
            out var plan,
            StrictOptions());

        Assert.True(succeeded);
        Assert.Equal(RoutePlanCompletionMode.PartialToFrontier, plan.CompletionMode);
        Assert.Equal("frontier", plan.FrontierNode?.NodeId);
        Assert.Equal(40, plan.Task!.Positions[^1].X, precision: 2);
        Assert.DoesNotContain(plan.Task.Positions, point => Math.Abs(point.X - 100) < 0.01);
        Assert.Contains(plan.CostBreakdown, item =>
            item.Component == "frontier-local-navigation" && item.Seconds > 0);
        Assert.Equal(plan.Cost, Math.Round(plan.CostBreakdown.Sum(item => item.Seconds), 2), precision: 2);
    }

    [Fact]
    public void TryPlan_ReturnsLocalOnlyWhenGraphCannotMakeAnyProgress()
    {
        var current = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 };
        var unrelatedA = new RouteNavigationNode { NodeId = "a", MapName = "Teyvat", X = 1000, Y = 0 };
        var unrelatedB = new RouteNavigationNode { NodeId = "b", MapName = "Teyvat", X = 1010, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [current, unrelatedA, unrelatedB],
                Edges = [CreateHistoricalEdge("unrelated", unrelatedA, unrelatedB)]
            },
            64,
            []);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(50, 0)),
            out var plan,
            StrictOptions());

        Assert.True(succeeded);
        Assert.Equal(RoutePlanCompletionMode.LocalOnly, plan.CompletionMode);
        Assert.Null(plan.Task);
        Assert.Equal("current", plan.FrontierNode?.NodeId);
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

    [Fact]
    public void TryPlan_SelectsTeleportWithLowestTotalWalkingTime()
    {
        var snapshot = CreateTeleportSnapshot();
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(200, 0)),
            out var plan,
            TeleportOptions());

        Assert.True(succeeded);
        Assert.True(plan.UsesTeleport);
        Assert.Equal("fast", plan.Teleport?.Name);
        Assert.Contains(plan.CostBreakdown, item =>
            item.Component == "teleport" && item.Source == RouteNavigationCostSource.Estimated);
    }

    [Fact]
    public void TryPlan_EmitsAtMostOneTeleportWaypoint()
    {
        var planner = new RouteNavigationPlanner(
            new FakeGraphProvider(CreateTeleportSnapshot()),
            new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(200, 0)),
            out var plan,
            TeleportOptions());

        Assert.True(succeeded);
        Assert.NotNull(plan.Task);
        Assert.Single(plan.Task!.Positions.Where(position => position.Type == WaypointType.Teleport.Code));
    }

    [Fact]
    public void TryPlan_WithoutCurrentPositionStartsFromTeleportAndDoesNotConvertPlaceholder()
    {
        var converter = new FakeCoordinateConverter(failAtImageX: -999);
        var planner = new RouteNavigationPlanner(
            new FakeGraphProvider(CreateTeleportSnapshot()),
            converter);
        var request = CreateRequest(new RouteGraphPoint(-999, 0), new RouteGraphPoint(200, 0));
        request = new RouteNavigationPlanRequest
        {
            MapName = request.MapName,
            MapMatchMethod = request.MapMatchMethod,
            CurrentImagePoint = request.CurrentImagePoint,
            HasCurrentPosition = false,
            TargetImagePoint = request.TargetImagePoint,
            TaskName = request.TaskName
        };

        var succeeded = planner.TryPlan(request, out var plan, TeleportOptions());

        Assert.True(succeeded, plan.FailureReason);
        Assert.True(plan.UsesTeleport);
        Assert.Equal(WaypointType.Teleport.Code, plan.Task!.Positions[0].Type);
        Assert.DoesNotContain(plan.Segments, segment =>
            segment.Polyline.Contains(new RouteGraphPoint(-999, 0)));
    }

    [Fact]
    public void TryPlan_WithoutCurrentPositionFallsBackToNearestTargetTeleportWithoutGraphEntry()
    {
        var from = new RouteNavigationNode { NodeId = "far-a", MapName = "Teyvat", X = 5000, Y = 0 };
        var to = new RouteNavigationNode { NodeId = "far-b", MapName = "Teyvat", X = 5010, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [from, to],
                Edges = [CreateHistoricalEdge("far", from, to)]
            },
            64,
            [CreateTeleport("no-entry", "near-target", 950)]);
        var planner = new RouteNavigationPlanner(
            new FakeGraphProvider(snapshot),
            new IdentityCoordinateConverter());
        var request = new RouteNavigationPlanRequest
        {
            MapName = "Teyvat",
            CurrentImagePoint = new RouteGraphPoint(1000, 0),
            HasCurrentPosition = false,
            TargetImagePoint = new RouteGraphPoint(1000, 0)
        };

        var succeeded = planner.TryPlan(request, out var plan, TeleportOptions());

        Assert.True(succeeded, plan.FailureReason);
        Assert.Equal(RoutePlanCompletionMode.LocalOnly, plan.CompletionMode);
        Assert.Equal("near-target", plan.Teleport?.Name);
        Assert.Equal(WaypointType.Teleport.Code, plan.Task!.Positions[0].Type);
    }

    [Fact]
    public void TryPlan_PrefersLastRouteAdjacentTeleportEvenWhenCurrentRouteIsShort()
    {
        var current = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 };
        var teleport = new RouteNavigationNode
        {
            NodeId = "teleport",
            MapName = "Teyvat",
            X = 9,
            Y = 0,
            AnchorIds = ["tp"]
        };
        var target = new RouteNavigationNode { NodeId = "target", MapName = "Teyvat", X = 10, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [current, teleport, target],
                Edges =
                [
                    CreateHistoricalEdge("current-target", current, target),
                    CreateHistoricalEdge("teleport-target", teleport, target)
                ]
            },
            64,
            [CreateTeleport("tp", "near", 9)]);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(10, 0)),
            out var plan,
            TeleportOptions());

        Assert.True(succeeded);
        Assert.True(plan.UsesTeleport);
        Assert.Equal("near", plan.Teleport?.Name);
        Assert.Single(plan.Task!.Positions.Where(position => position.Type == WaypointType.Teleport.Code));
    }

    [Fact]
    public void TryPlan_SelectsLowestTotalIncludingTargetConnectorTime()
    {
        var current = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 };
        var quickButFar = new RouteNavigationNode { NodeId = "quick-far", MapName = "Teyvat", X = 32, Y = 0 };
        var slowerButExact = new RouteNavigationNode { NodeId = "slower-exact", MapName = "Teyvat", X = 50, Y = 0 };
        var quickEdge = CreateHistoricalEdge("quick", current, quickButFar);
        quickEdge.SourceKind = "telemetry";
        quickEdge.AverageDurationMs = 1_000;
        var exactEdge = CreateHistoricalEdge("exact", current, slowerButExact);
        exactEdge.SourceKind = "telemetry";
        exactEdge.AverageDurationMs = 2_000;
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [current, quickButFar, slowerButExact],
                Edges = [quickEdge, exactEdge]
            },
            64,
            []);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());
        var options = new RouteNavigationPlanOptions
        {
            AllowTeleport = false,
            AllowUnknownStartConnector = false,
            AllowUnknownTargetConnector = false,
            CurrentAttachMaxDistance = 2,
            TargetAttachMaxDistance = 20,
            OutputPointMinDistance = 0,
            TargetOutputMinDistance = 0
        };

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(50, 0)),
            out var plan,
            options);

        Assert.True(succeeded);
        Assert.Equal("slower-exact", plan.FrontierNode?.NodeId);
        Assert.Equal("exact", Assert.Single(plan.Edges).EdgeId);
    }

    [Fact]
    public void TryPlan_LastRouteAdjacentTeleportTakesPriorityOverEarlierCostCandidate()
    {
        var current = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = -1000, Y = 0 };
        var target = new RouteNavigationNode { NodeId = "target", MapName = "Teyvat", X = 100, Y = 0 };
        var usefulTeleportNode = new RouteNavigationNode
        {
            NodeId = "useful",
            MapName = "Teyvat",
            X = 0,
            Y = 0,
            AnchorIds = ["useful-tp"]
        };
        var nodes = new List<RouteNavigationNode> { current, target, usefulTeleportNode };
        var teleports = new List<RouteGraphTeleportEntry> { CreateTeleport("useful-tp", "useful", 0) };
        for (var index = 1; index <= 30; index++)
        {
            var anchor = $"unused-{index}";
            nodes.Add(new RouteNavigationNode
            {
                NodeId = anchor,
                MapName = "Teyvat",
                X = 95 - index,
                Y = 0,
                AnchorIds = [anchor]
            });
            teleports.Add(CreateTeleport(anchor, anchor, 95 - index));
        }

        var currentEdge = CreateHistoricalEdge("current-target", current, target);
        var teleportEdge = CreateHistoricalEdge("useful-target", usefulTeleportNode, target);
        teleportEdge.SourceKind = "telemetry";
        teleportEdge.AverageDurationMs = 100;
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph { Nodes = nodes, Edges = [currentEdge, teleportEdge] },
            64,
            teleports);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(-1000, 0), new RouteGraphPoint(100, 0)),
            out var plan,
            TeleportOptions());

        Assert.True(succeeded);
        Assert.True(plan.UsesTeleport);
        Assert.Equal("unused-1", plan.Teleport?.Name);
    }

    [Fact]
    public void TryPlan_PrefersNearestTargetTeleportLocalOverFarAnchoredGraphRoute()
    {
        var city = new RouteNavigationNode
        {
            NodeId = "city",
            MapName = "Teyvat",
            X = 0,
            Y = 0,
            AnchorIds = ["city-tp"]
        };
        var target = new RouteNavigationNode { NodeId = "target", MapName = "Teyvat", X = 100, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [city, target],
                Edges = [CreateHistoricalEdge("city-target", city, target)]
            },
            64,
            [
                CreateTeleport("city-tp", "mondstadt-city", 0),
                CreateTeleport("near-target", "near-target", 95)
            ]);
        var planner = new RouteNavigationPlanner(
            new FakeGraphProvider(snapshot),
            new IdentityCoordinateConverter());
        var request = new RouteNavigationPlanRequest
        {
            MapName = "Teyvat",
            CurrentImagePoint = new RouteGraphPoint(100, 0),
            HasCurrentPosition = false,
            TargetImagePoint = new RouteGraphPoint(100, 0)
        };

        var succeeded = planner.TryPlan(request, out var plan, TeleportOptions());

        Assert.True(succeeded, plan.FailureReason);
        Assert.Equal(RoutePlanCompletionMode.LocalOnly, plan.CompletionMode);
        Assert.Equal("near-target", plan.Teleport?.Name);
        Assert.Equal(WaypointType.Teleport.Code, plan.Task!.Positions[0].Type);
    }

    [Fact]
    public void TryPlan_TrimsCurrentRouteAtLastTeleportNearItsPolyline()
    {
        var start = new RouteNavigationNode { NodeId = "start", MapName = "Teyvat", X = 0, Y = 0 };
        var first = new RouteNavigationNode { NodeId = "first", MapName = "Teyvat", X = 100, Y = 0 };
        var last = new RouteNavigationNode { NodeId = "last", MapName = "Teyvat", X = 200, Y = 0 };
        var target = new RouteNavigationNode { NodeId = "target", MapName = "Teyvat", X = 300, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [start, first, last, target],
                Edges =
                [
                    CreateHistoricalEdge("before-first", start, first),
                    CreateHistoricalEdge("between", first, last),
                    CreateHistoricalEdge("after-last", last, target)
                ]
            },
            64,
            [CreateTeleport("tp-first", "first", 101), CreateTeleport("tp-last", "last", 201)]);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(300, 0)),
            out var plan,
            TeleportOptions());

        Assert.True(succeeded, plan.FailureReason);
        Assert.True(plan.UsesTeleport);
        Assert.Equal("last", plan.Teleport?.Name);
        Assert.Equal(WaypointType.Teleport.Code, plan.Task!.Positions[0].Type);
        Assert.DoesNotContain(plan.Task.Positions.Skip(1), point => point.X < 190);
    }

    [Fact]
    public void TryPlan_WhenGraphIsFar_UsesNearestTargetTeleportThenLocalNavigation()
    {
        var farA = new RouteNavigationNode { NodeId = "far-a", MapName = "Teyvat", X = 5000, Y = 0 };
        var farB = new RouteNavigationNode { NodeId = "far-b", MapName = "Teyvat", X = 5010, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [farA, farB],
                Edges = [CreateHistoricalEdge("far", farA, farB)]
            },
            64,
            [CreateTeleport("near-target", "near-target", 950), CreateTeleport("far-target", "far-target", 700)]);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(1000, 0)),
            out var plan,
            TeleportOptions());

        Assert.True(succeeded, plan.FailureReason);
        Assert.Equal(RoutePlanCompletionMode.LocalOnly, plan.CompletionMode);
        Assert.Equal("near-target", plan.Teleport?.Name);
        Assert.Equal(2, plan.Task!.Positions.Count);
        Assert.Equal(WaypointType.Teleport.Code, plan.Task.Positions[0].Type);
        Assert.Equal(950, plan.FrontierNode!.X, precision: 2);
    }

    [Fact]
    public void TryPlan_WhenGraphIsFar_AllowsCurrentLocalNavigationOnlyForNearbyTarget()
    {
        var farA = new RouteNavigationNode { NodeId = "far-a", MapName = "Teyvat", X = 5000, Y = 0 };
        var farB = new RouteNavigationNode { NodeId = "far-b", MapName = "Teyvat", X = 5010, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph { Nodes = [farA, farB], Edges = [CreateHistoricalEdge("far", farA, farB)] },
            64,
            []);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(0, 0), new RouteGraphPoint(50, 0)),
            out var plan,
            TeleportOptions());

        Assert.True(succeeded, plan.FailureReason);
        Assert.Equal(RoutePlanCompletionMode.LocalOnly, plan.CompletionMode);
        Assert.False(plan.UsesTeleport);
        Assert.Null(plan.Task);
    }

    [Fact]
    public void TryPlan_AttachesToNearbyEdgeInsteadOfTreatingFarEndpointsAsNoGraph()
    {
        var from = new RouteNavigationNode { NodeId = "from", MapName = "Teyvat", X = 0, Y = 0 };
        var to = new RouteNavigationNode { NodeId = "to", MapName = "Teyvat", X = 200, Y = 0 };
        var snapshot = new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph { Nodes = [from, to], Edges = [CreateHistoricalEdge("long", from, to)] },
            64,
            []);
        var planner = new RouteNavigationPlanner(new FakeGraphProvider(snapshot), new IdentityCoordinateConverter());

        var succeeded = planner.TryPlan(
            CreateRequest(new RouteGraphPoint(100, 0), new RouteGraphPoint(200, 0)),
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

        Assert.True(succeeded, plan.FailureReason);
        Assert.Equal(RoutePlanCompletionMode.Complete, plan.CompletionMode);
        Assert.NotNull(plan.Task);
        Assert.DoesNotContain(plan.Task!.Positions, point => point.X < 99);
        Assert.Equal(200, plan.Task.Positions[^1].X, precision: 2);
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

    private static RouteNavigationPlanOptions TeleportOptions()
    {
        return new RouteNavigationPlanOptions
        {
            AllowTeleport = true,
            AllowUnknownStartConnector = false,
            AllowUnknownTargetConnector = false,
            CurrentAttachMaxDistance = 2,
            TargetAttachMaxDistance = 2,
            OutputPointMinDistance = 0,
            TargetOutputMinDistance = 0,
            CostOptions = new RouteNavigationCostOptions
            {
                WalkSpeed = 4.5,
                TeleportDurationSeconds = 18,
                MinimumTeleportSavingsSeconds = 8
            }
        };
    }

    private static RouteNavigationGraphSnapshot CreateTeleportSnapshot()
    {
        var current = new RouteNavigationNode { NodeId = "current", MapName = "Teyvat", X = 0, Y = 0 };
        var slow = new RouteNavigationNode
        {
            NodeId = "slow",
            MapName = "Teyvat",
            X = 100,
            Y = 0,
            AnchorIds = ["slow-tp"]
        };
        var fast = new RouteNavigationNode
        {
            NodeId = "fast",
            MapName = "Teyvat",
            X = 180,
            Y = 0,
            AnchorIds = ["fast-tp"]
        };
        var target = new RouteNavigationNode { NodeId = "target", MapName = "Teyvat", X = 200, Y = 0 };
        return new RouteNavigationGraphSnapshot(
            new RouteNavigationGraph
            {
                Nodes = [current, slow, fast, target],
                Edges =
                [
                    CreateHistoricalEdge("current-target", current, target),
                    CreateHistoricalEdge("slow-target", slow, target),
                    CreateHistoricalEdge("fast-target", fast, target)
                ]
            },
            64,
            [
                CreateTeleport("slow-tp", "slow", 100),
                CreateTeleport("fast-tp", "fast", 180)
            ]);
    }

    private static RouteNavigationEdge CreateHistoricalEdge(
        string edgeId,
        RouteNavigationNode from,
        RouteNavigationNode to)
    {
        return new RouteNavigationEdge
        {
            EdgeId = edgeId,
            FromNodeId = from.NodeId,
            ToNodeId = to.NodeId,
            MapName = "Teyvat",
            MoveMode = MoveModeEnum.Walk.Code,
            SourceKind = "historical-path",
            Points =
            [
                new TelemetryPoint2D { X = (float)from.X, Y = (float)from.Y },
                new TelemetryPoint2D { X = (float)to.X, Y = (float)to.Y }
            ]
        };
    }

    private static RouteGraphTeleportEntry CreateTeleport(string anchorId, string name, double x)
    {
        return new RouteGraphTeleportEntry(
            "Teyvat",
            anchorId,
            anchorId,
            name,
            "TeleportWaypoint",
            x,
            0,
            x,
            0,
            x,
            0,
            x,
            0);
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

    private sealed class IdentityCoordinateConverter : IRouteCoordinateConverter
    {
        public bool TryImageToGame(string mapName, string? mapMatchMethod, RouteGraphPoint imagePoint, out RouteGamePoint gamePoint)
        {
            gamePoint = new RouteGamePoint(imagePoint.X, imagePoint.Y);
            return true;
        }

        public bool TryGameToImage(string mapName, string? mapMatchMethod, RouteGamePoint gamePoint, out RouteGraphPoint imagePoint)
        {
            imagePoint = new RouteGraphPoint(gamePoint.X, gamePoint.Y);
            return true;
        }
    }
}
