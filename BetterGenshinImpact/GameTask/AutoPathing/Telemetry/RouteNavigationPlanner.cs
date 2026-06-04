using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteNavigationPlanner
{
    private readonly RouteNavigationGraphProvider _graphProvider;

    public RouteNavigationPlanner(RouteNavigationGraphProvider? graphProvider = null)
    {
        _graphProvider = graphProvider ?? new RouteNavigationGraphProvider();
    }

    public bool TryPlan(
        RouteNavigationPlanRequest request,
        out RouteNavigationPlan plan,
        RouteNavigationPlanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        options ??= new RouteNavigationPlanOptions();

        if (!_graphProvider.TryGetSnapshot(out var graph) || graph.IsEmpty)
        {
            plan = RouteNavigationPlan.Failed("navigation graph is empty");
            return false;
        }

        var starts = BuildStartCandidates(graph, request, options);
        if (starts.Count == 0)
        {
            plan = RouteNavigationPlan.Failed("no graph entry node found");
            return false;
        }

        var targets = BuildTargetCandidates(graph, request, options);
        if (targets.Count == 0)
        {
            plan = RouteNavigationPlan.Failed("no target frontier node found");
            return false;
        }

        if (!TrySearch(graph, starts, targets, options, out var searchResult))
        {
            plan = RouteNavigationPlan.Failed("no connected route found");
            return false;
        }

        if (!TryBuildTask(request, graph, searchResult, options, out var task, out var failureReason))
        {
            plan = RouteNavigationPlan.Failed(failureReason);
            return false;
        }

        plan = new RouteNavigationPlan
        {
            Succeeded = true,
            FailureReason = string.Empty,
            Task = task,
            Cost = Math.Round(searchResult.TotalCost, 2),
            Edges = searchResult.Edges,
            UsesTeleport = searchResult.Start.Teleport != null,
            Teleport = searchResult.Start.Teleport,
            RequiresUnknownStartConnector = searchResult.Start.RequiresUnknownConnector,
            RequiresUnknownTargetConnector = searchResult.Target.RequiresUnknownConnector,
            StartAttachDistance = Math.Round(searchResult.Start.AttachDistance, 2),
            TargetAttachDistance = Math.Round(searchResult.Target.AttachDistance, 2),
            FrontierNode = searchResult.Target.Node,
            TargetImagePoint = request.TargetImagePoint
        };
        return true;
    }

    private static List<RoutePlanStartCandidate> BuildStartCandidates(
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
            result.Add(new RoutePlanStartCandidate(
                candidate.Node,
                null,
                candidate.Distance,
                candidate.Distance * options.CurrentAttachCostWeight,
                false));
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
                result.Add(new RoutePlanStartCandidate(
                    candidate.Node,
                    null,
                    candidate.Distance,
                    candidate.Distance * options.UnknownConnectorCostWeight,
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
                result.Add(new RoutePlanStartCandidate(
                    entryNode,
                    teleportCandidate.Teleport,
                    0,
                    options.TeleportBaseCost + (teleportCandidate.Distance * options.TeleportTargetDistanceCostWeight),
                    false));
            }
        }

        return result
            .GroupBy(candidate => new
            {
                candidate.Node.NodeId,
                TeleportAnchorId = candidate.Teleport?.AnchorId ?? string.Empty,
                candidate.RequiresUnknownConnector
            })
            .Select(g => g.OrderBy(candidate => candidate.InitialCost).First())
            .OrderBy(candidate => candidate.InitialCost)
            .Take(options.MaxStartCandidates)
            .ToList();
    }

    private static List<RoutePlanTargetCandidate> BuildTargetCandidates(
        RouteNavigationGraphSnapshot graph,
        RouteNavigationPlanRequest request,
        RouteNavigationPlanOptions options)
    {
        var result = graph.FindNearestNodes(
                request.MapName,
                request.TargetImagePoint,
                options.TargetNodeCandidateLimit,
                options.TargetAttachMaxDistance)
            .Select(candidate => new RoutePlanTargetCandidate(
                candidate.Node,
                candidate.Distance,
                candidate.Distance * options.TargetAttachCostWeight,
                false))
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
            .Select(candidate => new RoutePlanTargetCandidate(
                candidate.Node,
                candidate.Distance,
                candidate.Distance * options.UnknownConnectorCostWeight,
                true))
            .ToList();
    }

    private static bool TrySearch(
        RouteNavigationGraphSnapshot graph,
        IReadOnlyList<RoutePlanStartCandidate> starts,
        IReadOnlyList<RoutePlanTargetCandidate> targets,
        RouteNavigationPlanOptions options,
        out RoutePlanSearchResult result)
    {
        var targetByNodeId = targets
            .GroupBy(t => t.Node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.AttachCost).First(), StringComparer.OrdinalIgnoreCase);

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
            var nodeId = queue.Dequeue();
            if (!distances.TryGetValue(nodeId, out var currentCost))
            {
                continue;
            }

            if (targetByNodeId.TryGetValue(nodeId, out var target))
            {
                var edges = ReconstructEdges(previous, nodeId);
                var startNodeId = ResolveStartNodeId(previous, nodeId);
                result = new RoutePlanSearchResult(
                    startByNodeId[startNodeId],
                    target,
                    edges,
                    currentCost + target.AttachCost);
                return true;
            }

            foreach (var edge in graph.GetOutgoingEdges(nodeId))
            {
                if (!CanUseEdge(edge, options))
                {
                    continue;
                }

                var edgeCost = ResolveEdgeCost(edge);
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

        result = RoutePlanSearchResult.Empty;
        return false;
    }

    private static bool CanUseEdge(RouteNavigationEdge edge, RouteNavigationPlanOptions options)
    {
        if (string.Equals(edge.HealthStatus, RouteHealthStatus.Disabled, StringComparison.OrdinalIgnoreCase))
        {
            return options.AllowDisabledEdges;
        }

        return true;
    }

    private static double ResolveEdgeCost(RouteNavigationEdge edge)
    {
        if (edge.Cost > 0)
        {
            return edge.Cost;
        }

        if (edge.AverageDurationMs > 0)
        {
            return edge.AverageDurationMs / 1000.0;
        }

        if (edge.AverageDistance > 0)
        {
            return edge.AverageDistance;
        }

        return RouteGraphGeometry.PolylineDistance(ToRoutePoints(edge.Points));
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

    private static bool TryBuildTask(
        RouteNavigationPlanRequest request,
        RouteNavigationGraphSnapshot graph,
        RoutePlanSearchResult searchResult,
        RouteNavigationPlanOptions options,
        out PathingTask task,
        out string failureReason)
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

        var map = MapManager.GetMap(task.Info.MapName, task.Info.MapMatchMethod);
        if (map == null)
        {
            failureReason = "map provider not found";
            return false;
        }

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
        else if (!TryAddImageWaypoint(task.Positions, map, request.CurrentImagePoint, WaypointType.Path.Code, MoveModeEnum.Walk.Code, null, null, emittedImagePoints, 0))
        {
            failureReason = "current point coordinate conversion failed";
            return false;
        }

        foreach (var edge in searchResult.Edges)
        {
            var points = ResolveEdgePoints(graph, edge);
            foreach (var point in points)
            {
                TryAddImageWaypoint(
                    task.Positions,
                    map,
                    point,
                    WaypointType.Path.Code,
                    string.IsNullOrWhiteSpace(edge.MoveMode) ? MoveModeEnum.Walk.Code : edge.MoveMode,
                    null,
                    null,
                    emittedImagePoints,
                    options.OutputPointMinDistance);
            }
        }

        TryAddImageWaypoint(
            task.Positions,
            map,
            request.TargetImagePoint,
            WaypointType.Target.Code,
            ResolveTargetMoveMode(request, searchResult),
            request.TargetAction,
            request.TargetActionParams,
            emittedImagePoints,
            options.TargetOutputMinDistance);

        if (task.Positions.Count < 2)
        {
            failureReason = "planned task has insufficient points";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static string ResolveTargetMoveMode(RouteNavigationPlanRequest request, RoutePlanSearchResult searchResult)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetMoveMode))
        {
            return request.TargetMoveMode;
        }

        var lastEdgeMoveMode = searchResult.Edges.LastOrDefault()?.MoveMode;
        return string.IsNullOrWhiteSpace(lastEdgeMoveMode) ? MoveModeEnum.Walk.Code : lastEdgeMoveMode;
    }

    private static bool TryAddImageWaypoint(
        List<Waypoint> waypoints,
        Common.Map.Maps.Base.ISceneMap map,
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
            return true;
        }

        var gamePoint = map.ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f((float)imagePoint.X, (float)imagePoint.Y));
        if (gamePoint == null)
        {
            return false;
        }

        waypoints.Add(new Waypoint
        {
            X = Math.Round(gamePoint.Value.X, 2),
            Y = Math.Round(gamePoint.Value.Y, 2),
            Type = type,
            MoveMode = moveMode,
            Action = action,
            ActionParams = actionParams
        });
        emittedImagePoints.Add(imagePoint);
        return true;
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
}

public sealed class RouteNavigationPlanOptions
{
    public bool AllowTeleport { get; init; } = true;

    public bool AllowDisabledEdges { get; init; }

    public bool AllowUnknownStartConnector { get; init; } = true;

    public bool AllowUnknownTargetConnector { get; init; } = true;

    public int CurrentNodeCandidateLimit { get; init; } = 8;

    public int TargetNodeCandidateLimit { get; init; } = 8;

    public int TeleportCandidateLimit { get; init; } = 8;

    public int MaxStartCandidates { get; init; } = 24;

    public double CurrentAttachMaxDistance { get; init; } = 18;

    public double TargetAttachMaxDistance { get; init; } = 18;

    public double UnknownConnectorMaxDistance { get; init; } = 180;

    public double TeleportSearchMaxDistance { get; init; } = 0;

    public double CurrentAttachCostWeight { get; init; } = 1.0;

    public double TargetAttachCostWeight { get; init; } = 1.0;

    public double UnknownConnectorCostWeight { get; init; } = 8.0;

    public double TeleportBaseCost { get; init; } = 15.0;

    public double TeleportTargetDistanceCostWeight { get; init; } = 0.1;

    public double OutputPointMinDistance { get; init; } = 3.0;

    public double TargetOutputMinDistance { get; init; } = 2.0;
}

public sealed class RouteNavigationPlan
{
    public bool Succeeded { get; init; }

    public string FailureReason { get; init; } = string.Empty;

    public PathingTask? Task { get; init; }

    public double Cost { get; init; }

    public List<RouteNavigationEdge> Edges { get; init; } = [];

    public bool UsesTeleport { get; init; }

    public RouteGraphTeleportEntry? Teleport { get; init; }

    public bool RequiresUnknownStartConnector { get; init; }

    public bool RequiresUnknownTargetConnector { get; init; }

    public double StartAttachDistance { get; init; }

    public double TargetAttachDistance { get; init; }

    public RouteNavigationNode? FrontierNode { get; init; }

    public RouteGraphPoint TargetImagePoint { get; init; }

    public static RouteNavigationPlan Failed(string reason)
    {
        return new RouteNavigationPlan
        {
            Succeeded = false,
            FailureReason = reason
        };
    }
}

internal sealed record RoutePlanStartCandidate(
    RouteNavigationNode Node,
    RouteGraphTeleportEntry? Teleport,
    double AttachDistance,
    double InitialCost,
    bool RequiresUnknownConnector);

internal sealed record RoutePlanTargetCandidate(
    RouteNavigationNode Node,
    double AttachDistance,
    double AttachCost,
    bool RequiresUnknownConnector);

internal sealed record RoutePlanPreviousStep(string PreviousNodeId, RouteNavigationEdge Edge);

internal sealed record RoutePlanSearchResult(
    RoutePlanStartCandidate Start,
    RoutePlanTargetCandidate Target,
    List<RouteNavigationEdge> Edges,
    double TotalCost)
{
    public static RoutePlanSearchResult Empty { get; } = new(
        new RoutePlanStartCandidate(new RouteNavigationNode(), null, 0, 0, false),
        new RoutePlanTargetCandidate(new RouteNavigationNode(), 0, 0, false),
        [],
        0);
}
