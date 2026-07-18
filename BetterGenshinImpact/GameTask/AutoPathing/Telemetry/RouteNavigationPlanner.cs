using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Model.MaskMap;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public interface IRouteNavigationPlanner
{
    bool TryPlan(
        RouteNavigationPlanRequest request,
        out RouteNavigationPlan plan,
        RouteNavigationPlanOptions? options = null);
}

public enum RouteNavigationFailureCode
{
    None,
    GraphFileMissing,
    GraphEmpty,
    GraphInvalid,
    CurrentPointNotConnected,
    TargetPointNotConnected,
    NoRoute,
    TeleportUnavailable,
    CoordinateConversionFailed,
    PlannedTaskInvalid,
    Unexpected
}

public sealed class RouteNavigationPlanner : IRouteNavigationPlanner
{
    private readonly IRouteNavigationGraphProvider _graphProvider;
    private readonly IRouteCoordinateConverter _coordinateConverter;
    private readonly IRouteNavigationCostModel _costModel;

    public RouteNavigationPlanner(
        IRouteNavigationGraphProvider? graphProvider = null,
        IRouteCoordinateConverter? coordinateConverter = null,
        IRouteNavigationCostModel? costModel = null)
    {
        _graphProvider = graphProvider ?? new RouteNavigationGraphProvider();
        _coordinateConverter = coordinateConverter ?? RouteNavigationCoordinateService.Instance;
        _costModel = costModel ?? new RouteNavigationCostModel(_coordinateConverter);
    }

    public bool TryPlan(
        RouteNavigationPlanRequest request,
        out RouteNavigationPlan plan,
        RouteNavigationPlanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        options ??= new RouteNavigationPlanOptions();

        if (!_graphProvider.TryGetSnapshot(out var graph, out var loadStatus) || graph.IsEmpty)
        {
            var failureCode = loadStatus switch
            {
                RouteNavigationGraphLoadStatus.FileMissing => RouteNavigationFailureCode.GraphFileMissing,
                RouteNavigationGraphLoadStatus.Invalid => RouteNavigationFailureCode.GraphInvalid,
                _ => RouteNavigationFailureCode.GraphEmpty
            };
            plan = RouteNavigationPlan.Failed(failureCode, GetFailureReason(failureCode), request, options);
            return false;
        }

        if (!_coordinateConverter.TryImageToGame(
                request.MapName,
                request.MapMatchMethod,
                request.CurrentImagePoint,
                out var currentGamePoint) ||
            !_coordinateConverter.TryImageToGame(
                request.MapName,
                request.MapMatchMethod,
                request.TargetImagePoint,
                out var targetGamePoint))
        {
            plan = RouteNavigationPlan.Failed(
                RouteNavigationFailureCode.CoordinateConversionFailed,
                GetFailureReason(RouteNavigationFailureCode.CoordinateConversionFailed),
                request,
                options);
            return false;
        }

        if (IsCurrentGraphOutsideSafeLocalRange(graph, request, options))
        {
            var targetDistance = Distance(currentGamePoint, targetGamePoint);
            if (targetDistance <= options.CostOptions.LocalDirectMaxGameDistance)
            {
                plan = CreateCurrentLocalPlan(request, options, targetDistance);
                return true;
            }

            if (TryCreateTargetTeleportLocalPlan(graph, request, options, out plan))
            {
                return true;
            }

            var failureCode = options.AllowTeleport
                ? RouteNavigationFailureCode.TeleportUnavailable
                : RouteNavigationFailureCode.CurrentPointNotConnected;
            plan = RouteNavigationPlan.Failed(failureCode, GetFailureReason(failureCode), request, options);
            return false;
        }

        var starts = BuildStartCandidates(graph, request, options);
        if (starts.Count == 0)
        {
            var failureCode = HasUnavailableTeleportCandidate(graph, request, options)
                ? RouteNavigationFailureCode.TeleportUnavailable
                : RouteNavigationFailureCode.CurrentPointNotConnected;
            plan = RouteNavigationPlan.Failed(failureCode, GetFailureReason(failureCode), request, options);
            return false;
        }

        var targets = BuildTargetCandidates(graph, request, options);

        var currentStarts = starts.Where(candidate => candidate.Teleport == null).ToList();
        var teleportStarts = starts.Where(candidate => candidate.Teleport != null).ToList();
        var currentResult = RoutePlanSearchResult.Empty;
        var teleportResult = RoutePlanSearchResult.Empty;
        var hasCurrentRoute = targets.Count > 0 &&
                              TrySearch(graph, request, currentStarts, targets, options, out currentResult);
        var hasTeleportRoute = targets.Count > 0 &&
                               TrySearch(graph, request, teleportStarts, targets, options, out teleportResult);
        var completionMode = RoutePlanCompletionMode.Complete;
        if (!hasCurrentRoute && !hasTeleportRoute)
        {
            completionMode = RoutePlanCompletionMode.PartialToFrontier;
            hasCurrentRoute = TrySearchFrontier(graph, request, currentStarts, options, out currentResult);
            hasTeleportRoute = TrySearchFrontier(graph, request, teleportStarts, options, out teleportResult);
        }

        if (!hasCurrentRoute && !hasTeleportRoute)
        {
            plan = RouteNavigationPlan.Failed(
                RouteNavigationFailureCode.NoRoute,
                GetFailureReason(RouteNavigationFailureCode.NoRoute),
                request,
                options);
            return false;
        }

        var searchResult = SelectBestSearchResult(
            hasCurrentRoute ? currentResult : null,
            hasTeleportRoute ? teleportResult : null,
            options.CostOptions.MinimumTeleportSavingsSeconds);
        if (hasCurrentRoute &&
            options.AllowTeleport &&
            TryCreateLastRouteTeleportShortcut(graph, request, currentResult, options, out var routeShortcut))
        {
            searchResult = routeShortcut;
        }

        if (completionMode == RoutePlanCompletionMode.PartialToFrontier &&
            searchResult.Start.Teleport == null &&
            searchResult.Edges.Count == 0 &&
            string.Equals(
                searchResult.Start.Node.NodeId,
                searchResult.Target.Node.NodeId,
                StringComparison.OrdinalIgnoreCase) &&
            searchResult.Start.AttachDistance <= 0.001)
        {
            var remainingDistance = Distance(currentGamePoint, targetGamePoint);
            if (remainingDistance <= options.CostOptions.LocalDirectMaxGameDistance)
            {
                plan = CreateCurrentLocalPlan(request, options, remainingDistance, searchResult.Target.Node);
                return true;
            }

            if (TryCreateTargetTeleportLocalPlan(graph, request, options, out plan))
            {
                return true;
            }

            plan = RouteNavigationPlan.Failed(
                RouteNavigationFailureCode.NoRoute,
                GetFailureReason(RouteNavigationFailureCode.NoRoute),
                request,
                options);
            return false;
        }

        if (!TryBuildTask(request, graph, searchResult, options, completionMode, out var task, out var taskFailureCode))
        {
            plan = RouteNavigationPlan.Failed(
                taskFailureCode,
                GetFailureReason(taskFailureCode),
                request,
                options);
            return false;
        }

        plan = new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = completionMode,
            FailureReason = string.Empty,
            Task = task,
            Cost = Math.Round(searchResult.TotalCost, 2),
            CostBreakdown = BuildCostBreakdown(request, searchResult, options, completionMode),
            Edges = searchResult.Edges,
            Segments = BuildPlanSegments(request, graph, searchResult, options, completionMode),
            UsesTeleport = searchResult.Start.Teleport != null,
            Teleport = searchResult.Start.Teleport,
            RequiresUnknownStartConnector = searchResult.Start.RequiresUnknownConnector,
            RequiresUnknownTargetConnector = searchResult.Target.RequiresUnknownConnector,
            StartAttachDistance = Math.Round(searchResult.Start.AttachDistance, 2),
            TargetAttachDistance = Math.Round(searchResult.Target.AttachDistance, 2),
            FrontierNode = searchResult.Target.Node,
            TargetImagePoint = request.TargetImagePoint,
            Request = request,
            Options = options
        };
        return true;
    }

    private static RoutePlanSearchResult SelectBestSearchResult(
        RoutePlanSearchResult? current,
        RoutePlanSearchResult? teleport,
        double minimumTeleportSavingsSeconds)
    {
        if (current == null)
        {
            return teleport!;
        }

        if (teleport == null)
        {
            return current;
        }

        return teleport.TotalCost + Math.Max(0, minimumTeleportSavingsSeconds) <= current.TotalCost
            ? teleport
            : current;
    }

    private bool IsCurrentGraphOutsideSafeLocalRange(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        RouteNavigationPlanOptions options)
    {
        var candidatePoints = graph.FindNearestNodes(request.MapName, request.CurrentImagePoint, 1)
            .Select(item => new RouteGraphPoint(item.Node.X, item.Node.Y))
            .Concat(graph.FindNearestEdges(
                    request.MapName,
                    request.CurrentImagePoint,
                    1,
                    Math.Max(options.UnknownConnectorMaxDistance, options.CurrentAttachMaxDistance))
                .Select(item => item.ProjectedPoint));
        var nearestGameDistance = candidatePoints
            .Select(point => _costModel.EvaluateConnector(
                request.MapName,
                request.MapMatchMethod,
                request.CurrentImagePoint,
                point,
                MoveModeEnum.Walk.Code,
                options.CostOptions,
                "current-nearest-graph"))
            .Where(cost => cost.IsValid)
            .Select(cost => cost.GameDistance)
            .DefaultIfEmpty(double.PositiveInfinity)
            .Min();
        return nearestGameDistance > options.CostOptions.LocalDirectMaxGameDistance;
    }

    private RouteNavigationPlan CreateCurrentLocalPlan(
        RouteNavigationPlanRequest request,
        RouteNavigationPlanOptions options,
        double targetDistance,
        RouteNavigationNode? frontierNode = null)
    {
        var localCost = _costModel.EvaluateConnector(
            request.MapName,
            request.MapMatchMethod,
            request.CurrentImagePoint,
            request.TargetImagePoint,
            ResolveTargetMoveMode(request, null),
            options.CostOptions,
            "local-navigation");
        return new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.LocalOnly,
            Cost = Math.Round(localCost.Seconds, 2),
            CostBreakdown = [localCost],
            FrontierNode = frontierNode ?? new RouteNavigationNode
            {
                NodeId = "local-current",
                MapName = request.MapName,
                X = request.CurrentImagePoint.X,
                Y = request.CurrentImagePoint.Y
            },
            TargetAttachDistance = Math.Round(targetDistance, 2),
            TargetImagePoint = request.TargetImagePoint,
            Request = request,
            Options = options
        };
    }

    private bool TryCreateTargetTeleportLocalPlan(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        RouteNavigationPlanOptions options,
        out RouteNavigationPlan plan)
    {
        plan = null!;
        if (!options.AllowTeleport)
        {
            return false;
        }

        var candidate = graph.FindNearestTeleports(request.MapName, request.TargetImagePoint, 0)
            .Select(item => new
            {
                item.Teleport,
                LocalCost = _costModel.EvaluateConnector(
                    request.MapName,
                    request.MapMatchMethod,
                    item.Teleport.SpawnImagePoint,
                    request.TargetImagePoint,
                    ResolveTargetMoveMode(request, null),
                    options.CostOptions,
                    "local-navigation")
            })
            .Where(item => item.LocalCost.IsValid)
            .OrderBy(item => item.LocalCost.GameDistance)
            .ThenBy(item => item.Teleport.AnchorId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (candidate == null)
        {
            return false;
        }

        var teleportCost = _costModel.EvaluateTeleport(options.CostOptions);
        var task = CreateTask(request);
        task.Positions.Add(new Waypoint
        {
            X = candidate.Teleport.GameX,
            Y = candidate.Teleport.GameY,
            Type = WaypointType.Teleport.Code,
            MoveMode = MoveModeEnum.Walk.Code
        });
        task.Positions.Add(new Waypoint
        {
            X = candidate.Teleport.SpawnGameX,
            Y = candidate.Teleport.SpawnGameY,
            Type = WaypointType.Target.Code,
            MoveMode = MoveModeEnum.Walk.Code
        });

        var frontier = new RouteNavigationNode
        {
            NodeId = $"local_{candidate.Teleport.AnchorId}",
            MapName = request.MapName,
            X = candidate.Teleport.SpawnImagePoint.X,
            Y = candidate.Teleport.SpawnImagePoint.Y,
            AnchorIds = [candidate.Teleport.AnchorId]
        };
        plan = new RouteNavigationPlan
        {
            Succeeded = true,
            CompletionMode = RoutePlanCompletionMode.LocalOnly,
            Task = task,
            Cost = Math.Round(teleportCost.Seconds + candidate.LocalCost.Seconds, 2),
            CostBreakdown = [teleportCost, candidate.LocalCost],
            Segments =
            [
                new RouteNavigationPlanSegment
                {
                    Kind = RouteNavigationPlanSegmentKind.Teleport,
                    From = request.CurrentImagePoint,
                    To = candidate.Teleport.SpawnImagePoint,
                    Teleport = candidate.Teleport,
                    Cost = teleportCost.Seconds,
                    Polyline = [request.CurrentImagePoint, candidate.Teleport.SpawnImagePoint]
                }
            ],
            UsesTeleport = true,
            Teleport = candidate.Teleport,
            FrontierNode = frontier,
            TargetAttachDistance = Math.Round(candidate.LocalCost.GameDistance, 2),
            TargetImagePoint = request.TargetImagePoint,
            Request = request,
            Options = options
        };
        return true;
    }

    private bool TryCreateLastRouteTeleportShortcut(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        RoutePlanSearchResult currentRoute,
        RouteNavigationPlanOptions options,
        out RoutePlanSearchResult shortcut)
    {
        shortcut = RoutePlanSearchResult.Empty;
        RouteTeleportMatch? lastMatch = null;
        for (var edgeIndex = 0; edgeIndex < currentRoute.Edges.Count; edgeIndex++)
        {
            var edgePoints = ResolveEdgePoints(graph, currentRoute.Edges[edgeIndex]);
            for (var pointIndex = 0; pointIndex < edgePoints.Count; pointIndex++)
            {
                var point = edgePoints[pointIndex];
                var teleport = graph.FindNearestTeleports(request.MapName, point, 0)
                    .Select(item => new
                    {
                        item.Teleport,
                        Distance = Math.Min(
                            RouteGraphGeometry.Distance(point, item.Teleport.ImagePoint),
                            RouteGraphGeometry.Distance(point, item.Teleport.SpawnImagePoint))
                    })
                    .Where(item => item.Distance <= options.RouteTeleportAttachMaxDistance)
                    .OrderBy(item => item.Distance)
                    .ThenBy(item => item.Teleport.AnchorId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (teleport != null)
                {
                    lastMatch = new RouteTeleportMatch(edgeIndex, pointIndex, point, teleport.Teleport);
                }
            }
        }

        if (lastMatch == null)
        {
            return false;
        }

        var remainingEdges = new List<RouteNavigationEdge>();
        var firstPoints = ResolveEdgePoints(graph, currentRoute.Edges[lastMatch.EdgeIndex]);
        if (lastMatch.PointIndex < firstPoints.Count - 1)
        {
            remainingEdges.Add(CreateTrailingEdge(
                currentRoute.Edges[lastMatch.EdgeIndex],
                firstPoints.Skip(lastMatch.PointIndex).ToList()));
        }
        remainingEdges.AddRange(currentRoute.Edges.Skip(lastMatch.EdgeIndex + 1));

        var startNode = new RouteNavigationNode
        {
            NodeId = $"shortcut_{lastMatch.Teleport.AnchorId}_{lastMatch.EdgeIndex}_{lastMatch.PointIndex}",
            MapName = request.MapName,
            X = lastMatch.Point.X,
            Y = lastMatch.Point.Y,
            AnchorIds = [lastMatch.Teleport.AnchorId]
        };
        var connector = _costModel.EvaluateConnector(
            request.MapName,
            request.MapMatchMethod,
            lastMatch.Teleport.SpawnImagePoint,
            lastMatch.Point,
            MoveModeEnum.Walk.Code,
            options.CostOptions,
            "teleport-entry-connector");
        if (!connector.IsValid)
        {
            return false;
        }

        var startCost = _costModel.EvaluateTeleport(options.CostOptions).Seconds + connector.Seconds;
        var totalCost = startCost + currentRoute.Target.AttachCost +
                        remainingEdges.Sum(edge => ResolveEdgeCost(request, edge, options));
        shortcut = new RoutePlanSearchResult(
            new RoutePlanStartCandidate(
                startNode,
                lastMatch.Teleport,
                connector.GameDistance,
                startCost,
                false),
            currentRoute.Target,
            remainingEdges,
            totalCost);
        return double.IsFinite(totalCost);
    }

    private static RouteNavigationEdge CreateTrailingEdge(RouteNavigationEdge source, IReadOnlyList<RouteGraphPoint> points)
    {
        return new RouteNavigationEdge
        {
            EdgeId = source.EdgeId + "_shortcut",
            SegmentId = source.SegmentId,
            FromNodeId = source.FromNodeId,
            ToNodeId = source.ToNodeId,
            MapName = source.MapName,
            MoveMode = source.MoveMode,
            Action = source.Action,
            ActionParams = source.ActionParams,
            IsSyntheticReverse = source.IsSyntheticReverse,
            ReviewStatus = source.ReviewStatus,
            HealthStatus = source.HealthStatus,
            SourceKind = "route-shortcut",
            SourceFileName = source.SourceFileName,
            Sources = source.Sources,
            Points = points.Select(point => new TelemetryPoint2D { X = (float)point.X, Y = (float)point.Y }).ToList()
        };
    }

    private PathingTask CreateTask(RouteNavigationPlanRequest request)
    {
        var task = new PathingTask
        {
            Info = new PathingTaskInfo
            {
                Name = string.IsNullOrWhiteSpace(request.TaskName) ? "全图导航临时路线" : request.TaskName,
                Type = PathingTaskType.Collect.Code,
                MapName = RouteGraphGeometry.NormalizeMapName(request.MapName),
                MapMatchMethod = request.MapMatchMethod ?? TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod,
                BgiVersion = Global.Version
            }
        };
        AppendResourceItem(task, request);
        return task;
    }

    private static double Distance(RouteGamePoint from, RouteGamePoint to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool HasUnavailableTeleportCandidate(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        RouteNavigationPlanOptions options)
    {
        if (!options.AllowTeleport)
        {
            return false;
        }

        var candidates = graph.FindNearestTeleports(
            request.MapName,
            request.TargetImagePoint,
            options.TeleportCandidateLimit,
            options.TeleportSearchMaxDistance);
        return candidates.Count > 0 && candidates.All(candidate =>
            graph.GetTeleportEntryNodes(candidate.Teleport.AnchorId).Count == 0);
    }

    private static string GetFailureReason(RouteNavigationFailureCode code)
    {
        return code switch
        {
            RouteNavigationFailureCode.GraphFileMissing => "navigation graph file is missing",
            RouteNavigationFailureCode.GraphEmpty => "navigation graph is empty",
            RouteNavigationFailureCode.GraphInvalid => "navigation graph is invalid",
            RouteNavigationFailureCode.CurrentPointNotConnected => "current point cannot attach to graph",
            RouteNavigationFailureCode.TargetPointNotConnected => "target point cannot attach to graph",
            RouteNavigationFailureCode.NoRoute => "no connected route found",
            RouteNavigationFailureCode.TeleportUnavailable => "teleport entry is unavailable",
            RouteNavigationFailureCode.CoordinateConversionFailed => "coordinate conversion failed",
            RouteNavigationFailureCode.PlannedTaskInvalid => "planned task has insufficient points",
            _ => "unexpected route planning failure"
        };
    }

    private List<RouteNavigationPlanSegment> BuildPlanSegments(
        RouteNavigationPlanRequest request,
        RouteNavigationGraphSnapshot graph,
        RoutePlanSearchResult searchResult,
        RouteNavigationPlanOptions options,
        RoutePlanCompletionMode completionMode)
    {
        var segments = new List<RouteNavigationPlanSegment>();
        var currentPoint = request.CurrentImagePoint;

        if (searchResult.Start.Teleport != null)
        {
            var teleportCost = _costModel.EvaluateTeleport(options.CostOptions);
            segments.Add(new RouteNavigationPlanSegment
            {
                Kind = RouteNavigationPlanSegmentKind.Teleport,
                From = request.CurrentImagePoint,
                To = searchResult.Start.Teleport.SpawnImagePoint,
                Teleport = searchResult.Start.Teleport,
                Cost = teleportCost.Seconds,
                Polyline = [request.CurrentImagePoint, searchResult.Start.Teleport.SpawnImagePoint]
            });
            currentPoint = searchResult.Start.Teleport.SpawnImagePoint;
        }

        var startPoint = ResolveStartPoint(searchResult.Start);
        if (searchResult.Start.RequiresUnknownConnector || RouteGraphGeometry.Distance(currentPoint, startPoint) > 0)
        {
            segments.Add(new RouteNavigationPlanSegment
            {
                Kind = searchResult.Start.RequiresUnknownConnector
                    ? RouteNavigationPlanSegmentKind.UnknownStartConnector
                    : RouteNavigationPlanSegmentKind.StartConnector,
                From = currentPoint,
                To = startPoint,
                Cost = EvaluateConnectorCost(
                    request,
                    currentPoint,
                    startPoint,
                    MoveModeEnum.Walk.Code,
                    options,
                    searchResult.Start.RequiresUnknownConnector ? options.UnknownConnectorCostWeight : options.CurrentAttachCostWeight,
                    "start-connector").Seconds,
                Polyline = [currentPoint, startPoint]
            });
        }

        foreach (var edge in searchResult.Edges)
        {
            var points = ResolveEdgePoints(graph, edge);
            if (points.Count < 2)
            {
                continue;
            }

            segments.Add(new RouteNavigationPlanSegment
            {
                Kind = RouteNavigationPlanSegmentKind.GraphEdge,
                From = points[0],
                To = points[^1],
                SourceEdgeId = edge.EdgeId,
                SourceSegmentId = edge.SegmentId,
                MoveMode = edge.MoveMode,
                Action = edge.Action,
                ActionParams = edge.ActionParams,
                HealthStatus = edge.HealthStatus,
                Cost = ResolveEdgeCost(request, edge, options),
                Polyline = points
            });
        }

        var targetAttachPoint = new RouteGraphPoint(searchResult.Target.Node.X, searchResult.Target.Node.Y);
        if (completionMode == RoutePlanCompletionMode.Complete &&
            (searchResult.Target.RequiresUnknownConnector || RouteGraphGeometry.Distance(targetAttachPoint, request.TargetImagePoint) > 0))
        {
            segments.Add(new RouteNavigationPlanSegment
            {
                Kind = searchResult.Target.RequiresUnknownConnector
                    ? RouteNavigationPlanSegmentKind.UnknownTargetConnector
                    : RouteNavigationPlanSegmentKind.TargetConnector,
                From = targetAttachPoint,
                To = request.TargetImagePoint,
                Cost = searchResult.Target.AttachCost,
                Polyline = [targetAttachPoint, request.TargetImagePoint]
            });
        }

        return segments;
    }

    private List<RouteNavigationCostBreakdown> BuildCostBreakdown(
        RouteNavigationPlanRequest request,
        RoutePlanSearchResult searchResult,
        RouteNavigationPlanOptions options,
        RoutePlanCompletionMode completionMode)
    {
        var result = new List<RouteNavigationCostBreakdown>();
        var currentPoint = request.CurrentImagePoint;
        if (searchResult.Start.Teleport != null)
        {
            result.Add(_costModel.EvaluateTeleport(options.CostOptions));
            currentPoint = searchResult.Start.Teleport.SpawnImagePoint;
        }

        var startPoint = ResolveStartPoint(searchResult.Start);
        if (RouteGraphGeometry.Distance(currentPoint, startPoint) > 0)
        {
            result.Add(EvaluateConnectorCost(
                request,
                currentPoint,
                startPoint,
                MoveModeEnum.Walk.Code,
                options,
                searchResult.Start.Teleport != null
                    ? 1
                    : searchResult.Start.RequiresUnknownConnector
                        ? options.UnknownConnectorCostWeight
                        : options.CurrentAttachCostWeight,
                searchResult.Start.RequiresUnknownConnector
                    ? "unknown-start-connector"
                    : "start-connector"));
        }

        foreach (var edge in searchResult.Edges)
        {
            var baseEdgeCost = _costModel.EvaluateEdge(
                request.MapName,
                request.MapMatchMethod,
                edge,
                options.CostOptions);
            result.Add(baseEdgeCost);
            var weightedSeconds = ResolveEdgeCost(request, edge, options);
            if (double.IsFinite(weightedSeconds) && weightedSeconds > baseEdgeCost.Seconds + 0.001)
            {
                result.Add(new RouteNavigationCostBreakdown(
                    $"edge-quality:{edge.EdgeId}",
                    weightedSeconds - baseEdgeCost.Seconds,
                    RouteNavigationCostSource.Estimated));
            }
        }

        var targetPoint = new RouteGraphPoint(searchResult.Target.Node.X, searchResult.Target.Node.Y);
        if (completionMode == RoutePlanCompletionMode.Complete &&
            RouteGraphGeometry.Distance(targetPoint, request.TargetImagePoint) > 0)
        {
            var connector = _costModel.EvaluateConnector(
                request.MapName,
                request.MapMatchMethod,
                targetPoint,
                request.TargetImagePoint,
                ResolveTargetMoveMode(request, searchResult),
                options.CostOptions,
                searchResult.Target.RequiresUnknownConnector
                    ? "unknown-target-connector"
                    : "target-connector");
            result.Add(connector with { Seconds = searchResult.Target.AttachCost });
        }

        return result;
    }

    private RouteNavigationCostBreakdown EvaluateConnectorCost(
        RouteNavigationPlanRequest request,
        RouteGraphPoint from,
        RouteGraphPoint to,
        string moveMode,
        RouteNavigationPlanOptions options,
        double multiplier,
        string component)
    {
        var cost = _costModel.EvaluateConnector(
            request.MapName,
            request.MapMatchMethod,
            from,
            to,
            moveMode,
            options.CostOptions,
            component);
        return cost with { Seconds = cost.Seconds * Math.Max(0, multiplier) };
    }

    private List<RoutePlanStartCandidate> BuildStartCandidates(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        RouteNavigationPlanOptions options)
    {
        var result = new List<RoutePlanStartCandidate>();
        var nearbyNodes = graph.FindNearestNodes(
            request.MapName,
            request.CurrentImagePoint,
            options.CurrentNodeCandidateLimit,
            options.CurrentAttachMaxDistance);

        foreach (var candidate in nearbyNodes)
        {
            var nodePoint = new RouteGraphPoint(candidate.Node.X, candidate.Node.Y);
            result.Add(new RoutePlanStartCandidate(
                candidate.Node,
                null,
                candidate.Distance,
                EvaluateConnectorCost(
                    request,
                    request.CurrentImagePoint,
                    nodePoint,
                    MoveModeEnum.Walk.Code,
                    options,
                    options.CurrentAttachCostWeight,
                    "start-connector").Seconds,
                false));
        }

        foreach (var projection in graph.FindNearestEdges(
                     request.MapName,
                     request.CurrentImagePoint,
                     options.CurrentNodeCandidateLimit,
                     Math.Max(options.UnknownConnectorMaxDistance, options.CurrentAttachMaxDistance)))
        {
            var connector = EvaluateConnectorCost(
                request,
                request.CurrentImagePoint,
                projection.ProjectedPoint,
                MoveModeEnum.Walk.Code,
                options,
                1,
                "start-edge-connector");
            if (!connector.IsValid || connector.GameDistance > options.CostOptions.LocalDirectMaxGameDistance)
            {
                continue;
            }

            var toNode = graph.GetNode(projection.Edge.ToNodeId);
            var edgePoints = ResolveEdgePoints(graph, projection.Edge);
            if (toNode == null || edgePoints.Count < 2)
            {
                continue;
            }
            var trailingPoints = new List<RouteGraphPoint> { projection.ProjectedPoint };
            trailingPoints.AddRange(edgePoints.Skip(projection.SegmentIndex + 1));
            if (trailingPoints.Count < 2)
            {
                continue;
            }
            var trailingEdge = CreateTrailingEdge(projection.Edge, trailingPoints);
            var trailingCost = ResolveEdgeCost(request, trailingEdge, options);
            result.Add(new RoutePlanStartCandidate(
                toNode,
                null,
                connector.GameDistance,
                connector.Seconds + trailingCost,
                projection.Distance > options.CurrentAttachMaxDistance,
                trailingEdge));
        }

        if (result.Count == 0 && options.AllowUnknownStartConnector)
        {
            var frontierNodes = graph.FindNearestNodes(
                request.MapName,
                request.CurrentImagePoint,
                options.CurrentNodeCandidateLimit,
                options.UnknownConnectorMaxDistance);

            foreach (var candidate in frontierNodes)
            {
                var nodePoint = new RouteGraphPoint(candidate.Node.X, candidate.Node.Y);
                result.Add(new RoutePlanStartCandidate(
                    candidate.Node,
                    null,
                    candidate.Distance,
                    EvaluateConnectorCost(
                        request,
                        request.CurrentImagePoint,
                        nodePoint,
                        MoveModeEnum.Walk.Code,
                        options,
                        options.UnknownConnectorCostWeight,
                        "unknown-start-connector").Seconds,
                    true));
            }
        }

        if (!options.AllowTeleport)
        {
            return result;
        }

        var teleportCandidates = graph.FindNearestTeleports(
            request.MapName,
            request.TargetImagePoint,
            options.TeleportCandidateLimit,
            options.TeleportSearchMaxDistance);

        foreach (var teleportCandidate in teleportCandidates)
        {
            var entryNodes = graph.GetTeleportEntryNodes(teleportCandidate.Teleport.AnchorId);
            foreach (var entryNode in entryNodes)
            {
                var entryPoint = new RouteGraphPoint(entryNode.X, entryNode.Y);
                var spawnConnector = EvaluateConnectorCost(
                    request,
                    teleportCandidate.Teleport.SpawnImagePoint,
                    entryPoint,
                    MoveModeEnum.Walk.Code,
                    options,
                    1,
                    "teleport-entry-connector");
                result.Add(new RoutePlanStartCandidate(
                    entryNode,
                    teleportCandidate.Teleport,
                    RouteGraphGeometry.Distance(teleportCandidate.Teleport.SpawnImagePoint, entryPoint),
                    _costModel.EvaluateTeleport(options.CostOptions).Seconds + spawnConnector.Seconds,
                    false));
            }
        }

        var orderedCandidates = result
            .Where(candidate => double.IsFinite(candidate.InitialCost))
            .GroupBy(candidate => new
            {
                candidate.Node.NodeId,
                TeleportAnchorId = candidate.Teleport?.AnchorId ?? string.Empty,
                candidate.RequiresUnknownConnector
            })
            .Select(g => g.OrderBy(candidate => candidate.InitialCost).First())
            .OrderBy(candidate => candidate.InitialCost);
        return (options.MaxStartCandidates <= 0
            ? orderedCandidates
            : orderedCandidates.Take(options.MaxStartCandidates)).ToList();
    }

    private List<RoutePlanTargetCandidate> BuildTargetCandidates(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        RouteNavigationPlanOptions options)
    {
        var semanticTargets = graph.FindResourceNodes(
                request.MapName,
                request.TargetResourceId,
                request.TargetResourceLabelId,
                request.TargetImagePoint,
                options.TargetNodeCandidateLimit,
                options.ResourceSemanticMaxDistance)
            .Select(node =>
            {
                var distance = RouteGraphGeometry.Distance(request.TargetImagePoint, new RouteGraphPoint(node.X, node.Y));
                var nodePoint = new RouteGraphPoint(node.X, node.Y);
                return new RoutePlanTargetCandidate(
                    node,
                    distance,
                    EvaluateConnectorCost(
                        request,
                        nodePoint,
                        request.TargetImagePoint,
                        ResolveTargetMoveMode(request, null),
                        options,
                        options.TargetAttachCostWeight * options.ResourceSemanticAttachCostMultiplier,
                        "target-connector").Seconds,
                    false,
                    true);
            })
            .ToList();

        if (semanticTargets.Count > 0)
        {
            return semanticTargets;
        }

        var result = graph.FindNearestNodes(
                request.MapName,
                request.TargetImagePoint,
                options.TargetNodeCandidateLimit,
                options.TargetAttachMaxDistance)
            .Select(candidate =>
            {
                var nodePoint = new RouteGraphPoint(candidate.Node.X, candidate.Node.Y);
                return new RoutePlanTargetCandidate(
                    candidate.Node,
                    candidate.Distance,
                    EvaluateConnectorCost(
                        request,
                        nodePoint,
                        request.TargetImagePoint,
                        ResolveTargetMoveMode(request, null),
                        options,
                        options.TargetAttachCostWeight,
                        "target-connector").Seconds,
                    false,
                    false);
            })
            .ToList();

        if (result.Count > 0 || !options.AllowUnknownTargetConnector)
        {
            return result;
        }

        return graph.FindNearestNodes(
                request.MapName,
                request.TargetImagePoint,
                options.TargetNodeCandidateLimit,
                options.UnknownConnectorMaxDistance)
            .Select(candidate =>
            {
                var nodePoint = new RouteGraphPoint(candidate.Node.X, candidate.Node.Y);
                return new RoutePlanTargetCandidate(
                    candidate.Node,
                    candidate.Distance,
                    EvaluateConnectorCost(
                        request,
                        nodePoint,
                        request.TargetImagePoint,
                        ResolveTargetMoveMode(request, null),
                        options,
                        options.UnknownConnectorCostWeight,
                        "unknown-target-connector").Seconds,
                    true,
                    false);
            })
            .ToList();
    }

    private bool TrySearch(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        IReadOnlyList<RoutePlanStartCandidate> starts,
        IReadOnlyList<RoutePlanTargetCandidate> targets,
        RouteNavigationPlanOptions options,
        out RoutePlanSearchResult result)
    {
        var targetByNodeId = targets
            .GroupBy(t => t.Node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.AttachCost).First(), StringComparer.OrdinalIgnoreCase);
        RoutePlanSearchResult? bestResult = null;
        var bestTotalCost = double.PositiveInfinity;

        var distances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, RoutePlanPreviousStep>(StringComparer.OrdinalIgnoreCase);
        var startByNodeId = new Dictionary<string, RoutePlanStartCandidate>(StringComparer.OrdinalIgnoreCase);
        var queue = new PriorityQueue<string, double>();

        foreach (var start in starts)
        {
            if (!distances.TryGetValue(start.Node.NodeId, out var currentCost) || start.InitialCost < currentCost)
            {
                distances[start.Node.NodeId] = start.InitialCost;
                startByNodeId[start.Node.NodeId] = start;
                queue.Enqueue(start.Node.NodeId, start.InitialCost);
            }
        }

        while (queue.Count > 0)
        {
            queue.TryDequeue(out var nodeId, out var dequeuedCost);
            if (!distances.TryGetValue(nodeId, out var currentCost) ||
                dequeuedCost > currentCost + 0.0001)
            {
                continue;
            }

            if (currentCost >= bestTotalCost)
            {
                break;
            }

            if (targetByNodeId.TryGetValue(nodeId, out var target))
            {
                var totalCost = currentCost + target.AttachCost;
                if (totalCost < bestTotalCost)
                {
                    var edges = ReconstructEdges(previous, nodeId);
                    var startNodeId = ResolveStartNodeId(previous, nodeId);
                    var start = startByNodeId[startNodeId];
                    PrependEntryEdge(edges, start);
                    bestResult = new RoutePlanSearchResult(
                        start,
                        target,
                        edges,
                        totalCost);
                    bestTotalCost = totalCost;
                }
            }

            foreach (var edge in graph.GetOutgoingEdges(nodeId))
            {
                if (!CanUseEdge(edge, options))
                {
                    continue;
                }

                var edgeCost = ResolveEdgeCost(request, edge, options);
                if (!double.IsFinite(edgeCost))
                {
                    continue;
                }
                var nextCost = currentCost + edgeCost;
                if (distances.TryGetValue(edge.ToNodeId, out var knownCost) && knownCost <= nextCost)
                {
                    continue;
                }

                distances[edge.ToNodeId] = nextCost;
                previous[edge.ToNodeId] = new RoutePlanPreviousStep(nodeId, edge);
                startByNodeId[edge.ToNodeId] = startByNodeId[nodeId];
                queue.Enqueue(edge.ToNodeId, nextCost);
            }
        }

        result = bestResult ?? RoutePlanSearchResult.Empty;
        return bestResult != null;
    }

    private bool TrySearchFrontier(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        IReadOnlyList<RoutePlanStartCandidate> starts,
        RouteNavigationPlanOptions options,
        out RoutePlanSearchResult result)
    {
        if (starts.Count == 0)
        {
            result = RoutePlanSearchResult.Empty;
            return false;
        }

        var distances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, RoutePlanPreviousStep>(StringComparer.OrdinalIgnoreCase);
        var startByNodeId = new Dictionary<string, RoutePlanStartCandidate>(StringComparer.OrdinalIgnoreCase);
        var queue = new PriorityQueue<string, double>();
        RouteNavigationNode? bestNode = null;
        var bestScore = double.PositiveInfinity;
        var bestTargetDistance = double.PositiveInfinity;

        foreach (var start in starts.Where(candidate => double.IsFinite(candidate.InitialCost)))
        {
            if (!distances.TryGetValue(start.Node.NodeId, out var known) || start.InitialCost < known)
            {
                distances[start.Node.NodeId] = start.InitialCost;
                startByNodeId[start.Node.NodeId] = start;
                queue.Enqueue(start.Node.NodeId, start.InitialCost);
            }
        }

        while (queue.Count > 0)
        {
            queue.TryDequeue(out var nodeId, out var dequeuedCost);
            if (!distances.TryGetValue(nodeId, out var currentCost) ||
                dequeuedCost > currentCost + 0.0001)
            {
                continue;
            }

            var node = graph.GetNode(nodeId);
            if (node != null)
            {
                var nodePoint = new RouteGraphPoint(node.X, node.Y);
                var remaining = _costModel.EvaluateConnector(
                    request.MapName,
                    request.MapMatchMethod,
                    nodePoint,
                    request.TargetImagePoint,
                    MoveModeEnum.Run.Code,
                    options.CostOptions,
                    "frontier-remaining");
                var targetDistance = RouteGraphGeometry.Distance(nodePoint, request.TargetImagePoint);
                var score = currentCost + remaining.Seconds * options.FrontierRemainingTimeWeight;
                if (double.IsFinite(score) &&
                    (score < bestScore - 0.001 ||
                     Math.Abs(score - bestScore) <= 0.001 && targetDistance < bestTargetDistance))
                {
                    bestNode = node;
                    bestScore = score;
                    bestTargetDistance = targetDistance;
                }
            }

            foreach (var edge in graph.GetOutgoingEdges(nodeId))
            {
                if (!CanUseEdge(edge, options))
                {
                    continue;
                }

                var edgeCost = ResolveEdgeCost(request, edge, options);
                if (!double.IsFinite(edgeCost))
                {
                    continue;
                }

                var nextCost = currentCost + edgeCost;
                if (distances.TryGetValue(edge.ToNodeId, out var knownCost) && knownCost <= nextCost)
                {
                    continue;
                }

                distances[edge.ToNodeId] = nextCost;
                previous[edge.ToNodeId] = new RoutePlanPreviousStep(nodeId, edge);
                startByNodeId[edge.ToNodeId] = startByNodeId[nodeId];
                queue.Enqueue(edge.ToNodeId, nextCost);
            }
        }

        if (bestNode == null || !distances.TryGetValue(bestNode.NodeId, out var totalCost))
        {
            result = RoutePlanSearchResult.Empty;
            return false;
        }

        var edges = ReconstructEdges(previous, bestNode.NodeId);
        var startNodeId = ResolveStartNodeId(previous, bestNode.NodeId);
        var selectedStart = startByNodeId[startNodeId];
        PrependEntryEdge(edges, selectedStart);
        result = new RoutePlanSearchResult(
            selectedStart,
            new RoutePlanTargetCandidate(bestNode, bestTargetDistance, 0, false, false),
            edges,
            totalCost);
        return true;
    }

    private static bool CanUseEdge(RouteNavigationEdge edge, RouteNavigationPlanOptions options)
    {
        if (edge.ReviewStatus is GraphReviewStatus.Disabled or GraphReviewStatus.Rejected ||
            string.Equals(edge.HealthStatus, RouteHealthStatus.Disabled, StringComparison.OrdinalIgnoreCase))
        {
            return options.AllowDisabledEdges;
        }

        return true;
    }

    private double ResolveEdgeCost(
        RouteNavigationPlanRequest request,
        RouteNavigationEdge edge,
        RouteNavigationPlanOptions options)
    {
        var baseCost = _costModel.EvaluateEdge(
            request.MapName,
            request.MapMatchMethod,
            edge,
            options.CostOptions).Seconds;
        var reviewMultiplier = edge.ReviewStatus switch
        {
            GraphReviewStatus.Verified => 1.0,
            GraphReviewStatus.Risky => Math.Max(1, options.CostOptions.RiskyEdgeCostMultiplier),
            GraphReviewStatus.Disabled or GraphReviewStatus.Rejected => double.PositiveInfinity,
            _ => Math.Max(1, options.CostOptions.UnreviewedEdgeCostMultiplier)
        };
        var reverseMultiplier = edge.IsSyntheticReverse
            ? Math.Max(1, options.CostOptions.SyntheticReverseCostMultiplier)
            : 1.0;
        return baseCost * reviewMultiplier * reverseMultiplier;
    }

    private static List<RouteNavigationEdge> ReconstructEdges(
        Dictionary<string, RoutePlanPreviousStep> previous,
        string targetNodeId)
    {
        var edges = new List<RouteNavigationEdge>();
        var current = targetNodeId;
        while (previous.TryGetValue(current, out var step))
        {
            edges.Add(step.Edge);
            current = step.PreviousNodeId;
        }

        edges.Reverse();
        return edges;
    }

    private static void PrependEntryEdge(List<RouteNavigationEdge> edges, RoutePlanStartCandidate start)
    {
        if (start.EntryEdge != null && (edges.Count == 0 || !ReferenceEquals(edges[0], start.EntryEdge)))
        {
            edges.Insert(0, start.EntryEdge);
        }
    }

    private static string ResolveStartNodeId(
        Dictionary<string, RoutePlanPreviousStep> previous,
        string targetNodeId)
    {
        var current = targetNodeId;
        while (previous.TryGetValue(current, out var step))
        {
            current = step.PreviousNodeId;
        }

        return current;
    }

    private bool TryBuildTask(
        RouteNavigationPlanRequest request,
        RouteNavigationGraphSnapshot graph,
        RoutePlanSearchResult searchResult,
        RouteNavigationPlanOptions options,
        RoutePlanCompletionMode completionMode,
        out PathingTask task,
        out RouteNavigationFailureCode failureCode)
    {
        task = new PathingTask
        {
            Info = new PathingTaskInfo
            {
                Name = string.IsNullOrWhiteSpace(request.TaskName) ? "全图导航临时路线" : request.TaskName,
                Type = PathingTaskType.Collect.Code,
                MapName = RouteGraphGeometry.NormalizeMapName(request.MapName),
                MapMatchMethod = request.MapMatchMethod ?? TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod,
                BgiVersion = Global.Version
            }
        };
        AppendResourceItem(task, request);

        var emittedImagePoints = new List<RouteGraphPoint>();
        if (searchResult.Start.Teleport != null)
        {
            task.Positions.Add(new Waypoint
            {
                X = searchResult.Start.Teleport.GameX,
                Y = searchResult.Start.Teleport.GameY,
                Type = WaypointType.Teleport.Code,
                MoveMode = MoveModeEnum.Walk.Code
            });
            emittedImagePoints.Add(searchResult.Start.Teleport.ImagePoint);
        }
        else if (!TryAddImageWaypoint(task.Positions, task.Info, request.CurrentImagePoint, WaypointType.Path.Code, MoveModeEnum.Walk.Code, null, null, emittedImagePoints, 0))
        {
            failureCode = RouteNavigationFailureCode.CoordinateConversionFailed;
            return false;
        }

        foreach (var edge in searchResult.Edges)
        {
            var points = ResolveEdgePoints(graph, edge);
            foreach (var point in points)
            {
                if (!TryAddImageWaypoint(
                    task.Positions,
                    task.Info,
                    point,
                    WaypointType.Path.Code,
                    string.IsNullOrWhiteSpace(edge.MoveMode) ? MoveModeEnum.Walk.Code : edge.MoveMode,
                    null,
                    null,
                    emittedImagePoints,
                    options.OutputPointMinDistance))
                {
                    failureCode = RouteNavigationFailureCode.CoordinateConversionFailed;
                    return false;
                }
            }
        }

        var executableTarget = completionMode == RoutePlanCompletionMode.Complete
            ? request.TargetImagePoint
            : new RouteGraphPoint(searchResult.Target.Node.X, searchResult.Target.Node.Y);
        var targetAction = completionMode == RoutePlanCompletionMode.Complete ? request.TargetAction : null;
        var targetActionParams = completionMode == RoutePlanCompletionMode.Complete ? request.TargetActionParams : null;
        if (!TryAddImageWaypoint(
                task.Positions,
                task.Info,
                executableTarget,
                WaypointType.Target.Code,
                ResolveTargetMoveMode(request, searchResult),
                targetAction,
                targetActionParams,
                emittedImagePoints,
                completionMode == RoutePlanCompletionMode.Complete ? options.TargetOutputMinDistance : 0))
        {
            failureCode = RouteNavigationFailureCode.CoordinateConversionFailed;
            return false;
        }

        if (task.Positions.Count < 2)
        {
            failureCode = RouteNavigationFailureCode.PlannedTaskInvalid;
            return false;
        }

        failureCode = RouteNavigationFailureCode.None;
        return true;
    }

    private static string ResolveTargetMoveMode(RouteNavigationPlanRequest request, RoutePlanSearchResult? searchResult)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetMoveMode))
        {
            return request.TargetMoveMode;
        }

        var lastEdgeMoveMode = searchResult?.Edges.LastOrDefault()?.MoveMode;
        return string.IsNullOrWhiteSpace(lastEdgeMoveMode) ? MoveModeEnum.Walk.Code : lastEdgeMoveMode;
    }

    private static void AppendResourceItem(PathingTask task, RouteNavigationPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetResourceName) &&
            string.IsNullOrWhiteSpace(request.TargetResourceId) &&
            string.IsNullOrWhiteSpace(request.TargetResourceLabelId))
        {
            return;
        }

        var materialName = !string.IsNullOrWhiteSpace(request.TargetResourceName)
            ? request.TargetResourceName
            : !string.IsNullOrWhiteSpace(request.TargetResourceLabelId)
                ? $"Label:{request.TargetResourceLabelId}"
                : $"Resource:{request.TargetResourceId}";

        task.Info.Items.Add(new MaterialInfo
        {
            Material = materialName,
            Count = "1"
        });
    }

    private bool TryAddImageWaypoint(
        List<Waypoint> waypoints,
        PathingTaskInfo taskInfo,
        RouteGraphPoint imagePoint,
        string type,
        string moveMode,
        string? action,
        string? actionParams,
        List<RouteGraphPoint> emittedImagePoints,
        double minDistance)
    {
        if (emittedImagePoints.Count > 0 &&
            RouteGraphGeometry.Distance(emittedImagePoints[^1], imagePoint) < minDistance)
        {
            if (string.Equals(type, WaypointType.Target.Code, StringComparison.OrdinalIgnoreCase) && waypoints.Count > 0)
            {
                if (!_coordinateConverter.TryImageToGame(
                        taskInfo.MapName,
                        taskInfo.MapMatchMethod,
                        imagePoint,
                        out var exactTargetGamePoint))
                {
                    return false;
                }

                var last = waypoints[^1];
                last.X = Math.Round(exactTargetGamePoint.X, 2);
                last.Y = Math.Round(exactTargetGamePoint.Y, 2);
                last.Type = type;
                last.MoveMode = moveMode;
                last.Action = action;
                last.ActionParams = actionParams;
                emittedImagePoints[^1] = imagePoint;
            }

            return true;
        }

        if (!_coordinateConverter.TryImageToGame(
                taskInfo.MapName,
                taskInfo.MapMatchMethod,
                imagePoint,
                out var gamePoint))
        {
            return false;
        }

        waypoints.Add(new Waypoint
        {
            X = Math.Round(gamePoint.X, 2),
            Y = Math.Round(gamePoint.Y, 2),
            Type = type,
            MoveMode = moveMode,
            Action = action,
            ActionParams = actionParams
        });
        emittedImagePoints.Add(imagePoint);
        return true;
    }

    private static RouteGraphPoint ResolveStartPoint(RoutePlanStartCandidate start)
    {
        if (start.EntryEdge?.Points is { Count: > 0 } points)
        {
            return new RouteGraphPoint(points[0].X, points[0].Y);
        }
        return new RouteGraphPoint(start.Node.X, start.Node.Y);
    }

    private static List<RouteGraphPoint> ResolveEdgePoints(RouteNavigationGraphSnapshot graph, RouteNavigationEdge edge)
    {
        if (edge.Points is { Count: >= 2 })
        {
            return ToRoutePoints(edge.Points);
        }

        var fromNode = graph.GetNode(edge.FromNodeId);
        var toNode = graph.GetNode(edge.ToNodeId);
        if (fromNode == null || toNode == null)
        {
            return [];
        }

        return
        [
            new RouteGraphPoint(fromNode.X, fromNode.Y),
            new RouteGraphPoint(toNode.X, toNode.Y)
        ];
    }

    private static List<RouteGraphPoint> ToRoutePoints(List<TelemetryPoint2D>? points)
    {
        if (points == null)
        {
            return [];
        }

        return points.Select(p => new RouteGraphPoint(p.X, p.Y)).ToList();
    }
}

public sealed class RouteNavigationPlanRequest
{
    public string MapName { get; init; } = "Teyvat";

    public string? MapMatchMethod { get; init; }

    public RouteGraphPoint CurrentImagePoint { get; init; }

    public RouteGraphPoint TargetImagePoint { get; init; }

    public string TaskName { get; init; } = "全图导航临时路线";

    public string? TargetMoveMode { get; init; }

    public string? TargetAction { get; init; }

    public string? TargetActionParams { get; init; }

    public string? TargetResourceId { get; init; }

    public string? TargetResourceLabelId { get; init; }

    public string? TargetResourceName { get; init; }

    public static RouteNavigationPlanRequest FromMaskMapPoint(
        MaskMapPoint point,
        RouteGraphPoint currentImagePoint,
        string mapName,
        string? mapMatchMethod = null,
        RouteResourceCollectStrategy? strategy = null,
        string? resourceName = null,
        string taskName = "资源点路网导航")
    {
        ArgumentNullException.ThrowIfNull(point);
        strategy ??= RouteResourceCollectStrategy.ResolveDefault(point.LabelId, resourceName);

        return new RouteNavigationPlanRequest
        {
            MapName = mapName,
            MapMatchMethod = mapMatchMethod,
            CurrentImagePoint = currentImagePoint,
            TargetImagePoint = new RouteGraphPoint(point.ImageX, point.ImageY),
            TaskName = string.IsNullOrWhiteSpace(strategy.TaskName) ? taskName : strategy.TaskName,
            TargetMoveMode = strategy.MoveMode,
            TargetAction = strategy.Action,
            TargetActionParams = strategy.ActionParams,
            TargetResourceId = point.Id,
            TargetResourceLabelId = point.LabelId,
            TargetResourceName = string.IsNullOrWhiteSpace(strategy.ResourceName) ? resourceName : strategy.ResourceName
        };
    }
}

public sealed class RouteResourceCollectStrategy
{
    public static RouteResourceCollectStrategy Default { get; } = new();

    public string? Action { get; init; }

    public string? ActionParams { get; init; }

    public string? MoveMode { get; init; }

    public string? ResourceName { get; init; }

    public string? TaskName { get; init; }

    public static RouteResourceCollectStrategy ResolveDefault(string? labelId, string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return Default;
        }

        var normalizedName = resourceName.Trim();
        if (ContainsAny(normalizedName, "矿", "水晶", "魔晶", "铁块", "白铁", "紫晶", "萃凝晶"))
        {
            return new RouteResourceCollectStrategy
            {
                Action = ActionEnum.Mining.Code,
                ResourceName = normalizedName,
                TaskName = $"采集：{normalizedName}"
            };
        }

        if (ContainsAny(normalizedName, "钓鱼", "鱼"))
        {
            return new RouteResourceCollectStrategy
            {
                Action = ActionEnum.Fishing.Code,
                ResourceName = normalizedName,
                TaskName = $"采集：{normalizedName}"
            };
        }

        if (ContainsAny(normalizedName, "漂浮灵", "丘丘", "史莱姆", "骗骗花", "蕈兽", "圣骸", "镀金旅团", "愚人众", "龙蜥", "遗迹", "隙境原体"))
        {
            return new RouteResourceCollectStrategy
            {
                Action = ActionEnum.Fight.Code,
                ResourceName = normalizedName,
                TaskName = $"讨伐：{normalizedName}"
            };
        }

        if (ContainsAny(normalizedName, "蒲公英", "绯樱", "烈焰花", "冰雾花", "电气水晶"))
        {
            return new RouteResourceCollectStrategy
            {
                ResourceName = normalizedName,
                TaskName = $"采集：{normalizedName}"
            };
        }

        return new RouteResourceCollectStrategy
        {
            ResourceName = normalizedName,
            TaskName = $"采集：{normalizedName}"
        };
    }

    private static bool ContainsAny(string value, params string[] keywords)
    {
        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class RouteNavigationPlanOptions
{
    public RouteNavigationCostOptions CostOptions { get; init; } = new();

    public bool AllowTeleport { get; init; } = true;

    public bool AllowDisabledEdges { get; init; }

    public bool AllowUnknownStartConnector { get; init; } = true;

    public bool AllowUnknownTargetConnector { get; init; } = true;

    public int CurrentNodeCandidateLimit { get; init; } = 8;

    public int TargetNodeCandidateLimit { get; init; } = 8;

    /// <summary>0 表示评估地图内全部传送点，正数用于调试时限制候选数量。</summary>
    public int TeleportCandidateLimit { get; init; }

    /// <summary>0 表示不截断已经过筛选的起点候选。</summary>
    public int MaxStartCandidates { get; init; }

    public double CurrentAttachMaxDistance { get; init; } = 18;

    public double TargetAttachMaxDistance { get; init; } = 18;

    public double UnknownConnectorMaxDistance { get; init; } = 180;

    public double TeleportSearchMaxDistance { get; init; } = 0;

    /// <summary>路线点距离传送图标或落地点不超过该值时，可从这个传送点裁剪路线。</summary>
    public double RouteTeleportAttachMaxDistance { get; init; } = 10;

    public double CurrentAttachCostWeight { get; init; } = 1.0;

    public double TargetAttachCostWeight { get; init; } = 1.0;

    public double UnknownConnectorCostWeight { get; init; } = 8.0;

    public double OutputPointMinDistance { get; init; } = 3.0;

    public double TargetOutputMinDistance { get; init; } = 2.0;

    public double ResourceSemanticMaxDistance { get; init; } = 80.0;

    public double ResourceSemanticAttachCostMultiplier { get; init; } = 0.5;

    public double FrontierRemainingTimeWeight { get; init; } = 2.0;
}

public enum RoutePlanCompletionMode
{
    Complete,
    PartialToFrontier,
    LocalOnly,
    HardFailure
}

public sealed class RouteNavigationPlan
{
    public bool Succeeded { get; init; }

    public RoutePlanCompletionMode CompletionMode { get; init; }

    public RouteNavigationFailureCode FailureCode { get; init; }

    public string FailureReason { get; init; } = string.Empty;

    public PathingTask? Task { get; init; }

    public double Cost { get; init; }

    public List<RouteNavigationCostBreakdown> CostBreakdown { get; init; } = [];

    public List<RouteNavigationEdge> Edges { get; init; } = [];

    public List<RouteNavigationPlanSegment> Segments { get; init; } = [];

    public bool UsesTeleport { get; init; }

    public RouteGraphTeleportEntry? Teleport { get; init; }

    public bool RequiresUnknownStartConnector { get; init; }

    public bool RequiresUnknownTargetConnector { get; init; }

    public double StartAttachDistance { get; init; }

    public double TargetAttachDistance { get; init; }

    public RouteNavigationNode? FrontierNode { get; init; }

    public RouteGraphPoint TargetImagePoint { get; init; }

    public RouteNavigationPlanRequest? Request { get; init; }

    public RouteNavigationPlanOptions? Options { get; init; }

    public static RouteNavigationPlan Failed(
        RouteNavigationFailureCode code,
        string reason,
        RouteNavigationPlanRequest? request = null,
        RouteNavigationPlanOptions? options = null)
    {
        return new RouteNavigationPlan
        {
            Succeeded = false,
            CompletionMode = RoutePlanCompletionMode.HardFailure,
            FailureCode = code,
            FailureReason = reason,
            Request = request,
            Options = options
        };
    }

    public static RouteNavigationPlan Failed(string reason)
    {
        return Failed(RouteNavigationFailureCode.Unexpected, reason);
    }
}

public enum RouteNavigationPlanSegmentKind
{
    Teleport,
    StartConnector,
    UnknownStartConnector,
    GraphEdge,
    TargetConnector,
    UnknownTargetConnector
}

public sealed class RouteNavigationPlanSegment
{
    public RouteNavigationPlanSegmentKind Kind { get; init; }

    public RouteGraphPoint From { get; init; }

    public RouteGraphPoint To { get; init; }

    public string SourceEdgeId { get; init; } = string.Empty;

    public string SourceSegmentId { get; init; } = string.Empty;

    public string MoveMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string ActionParams { get; init; } = string.Empty;

    public string HealthStatus { get; init; } = string.Empty;

    public double Cost { get; init; }

    public RouteGraphTeleportEntry? Teleport { get; init; }

    public List<RouteGraphPoint> Polyline { get; init; } = [];
}

internal sealed record RoutePlanStartCandidate(
    RouteNavigationNode Node,
    RouteGraphTeleportEntry? Teleport,
    double AttachDistance,
    double InitialCost,
    bool RequiresUnknownConnector,
    RouteNavigationEdge? EntryEdge = null);

internal sealed record RoutePlanTargetCandidate(
    RouteNavigationNode Node,
    double AttachDistance,
    double AttachCost,
    bool RequiresUnknownConnector,
    bool MatchedResourceSemantic);

internal sealed record RoutePlanPreviousStep(string PreviousNodeId, RouteNavigationEdge Edge);

internal sealed record RouteTeleportMatch(
    int EdgeIndex,
    int PointIndex,
    RouteGraphPoint Point,
    RouteGraphTeleportEntry Teleport);

internal sealed record RoutePlanSearchResult(
    RoutePlanStartCandidate Start,
    RoutePlanTargetCandidate Target,
    List<RouteNavigationEdge> Edges,
    double TotalCost)
{
    public static RoutePlanSearchResult Empty { get; } = new(
        new RoutePlanStartCandidate(new RouteNavigationNode(), null, 0, 0, false),
        new RoutePlanTargetCandidate(new RouteNavigationNode(), 0, 0, false, false),
        [],
        0);
}
