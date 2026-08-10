using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public enum RouteGraphQualitySeverity
{
    Info,
    Warning,
    Error
}

public enum RouteGraphQualityIssueCode
{
    DuplicateNode,
    MissingEndpoint,
    SelfLoop,
    DuplicateEdge,
    IsolatedNode,
    SmallComponent,
    ExcessiveStraightEdge,
    CrossLayerEdge,
    SyntheticReverseNeedsReview,
    SameCoordinateDifferentLayer,
    TeleportWithoutEntry,
    TeleportEntryTooFar,
    CostLengthMismatch
}

public sealed record RouteGraphQualityIssue(
    RouteGraphQualityIssueCode Code,
    RouteGraphQualitySeverity Severity,
    string Message,
    string NodeId = "",
    string EdgeId = "",
    string MapName = "");

public sealed class RouteGraphQualityOptions
{
    public int SmallComponentNodeCount { get; init; } = 4;

    public double ExcessiveStraightEdgeDistance { get; init; } = 250;

    public double SameCoordinateTolerance { get; init; } = 2;

    public double TeleportEntryMaximumDistance { get; init; } = 10;

    public double CostLengthRatioMaximum { get; init; } = 8;
}

public sealed class RouteGraphQualityAnalyzer
{
    public IReadOnlyList<RouteGraphQualityIssue> Analyze(
        RouteNavigationGraph graph,
        IReadOnlyList<RouteGraphTeleportEntry>? teleports = null,
        RouteGraphQualityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new RouteGraphQualityOptions();
        teleports ??= [];
        var issues = new List<RouteGraphQualityIssue>();
        var nodes = graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeId))
            .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in graph.Nodes
                     .Where(node => !string.IsNullOrWhiteSpace(node.NodeId))
                     .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new RouteGraphQualityIssue(
                RouteGraphQualityIssueCode.DuplicateNode,
                RouteGraphQualitySeverity.Error,
                $"节点 ID {duplicate.Key} 重复 {duplicate.Count()} 次",
                duplicate.Key,
                MapName: duplicate.First().MapName));
        }
        var degree = nodes.Keys.ToDictionary(nodeId => nodeId, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var edge in graph.Edges)
        {
            if (!nodes.TryGetValue(edge.FromNodeId, out var from) || !nodes.TryGetValue(edge.ToNodeId, out var to))
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.MissingEndpoint,
                    RouteGraphQualitySeverity.Error,
                    $"边 {edge.EdgeId} 的端点不存在",
                    EdgeId: edge.EdgeId,
                    MapName: edge.MapName));
                continue;
            }

            degree[from.NodeId]++;
            degree[to.NodeId]++;
            if (string.Equals(from.NodeId, to.NodeId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.SelfLoop,
                    RouteGraphQualitySeverity.Error,
                    $"边 {edge.EdgeId} 形成自环",
                    from.NodeId,
                    edge.EdgeId,
                    edge.MapName));
            }

            if (!string.Equals(from.LayerId, to.LayerId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.CrossLayerEdge,
                    RouteGraphQualitySeverity.Warning,
                    $"边 {edge.EdgeId} 跨层 {from.LayerId} → {to.LayerId}",
                    EdgeId: edge.EdgeId,
                    MapName: edge.MapName));
            }

            var directDistance = RouteGraphGeometry.Distance(
                new RouteGraphPoint(from.X, from.Y),
                new RouteGraphPoint(to.X, to.Y));
            if (directDistance > options.ExcessiveStraightEdgeDistance && edge.Points.Count <= 2)
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.ExcessiveStraightEdge,
                    RouteGraphQualitySeverity.Warning,
                    $"边 {edge.EdgeId} 是 {directDistance:F1} 的异常长直线",
                    EdgeId: edge.EdgeId,
                    MapName: edge.MapName));
            }

            if (edge.IsSyntheticReverse && edge.ReviewStatus != GraphReviewStatus.Verified)
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.SyntheticReverseNeedsReview,
                    RouteGraphQualitySeverity.Warning,
                    $"合成反向边 {edge.EdgeId} 尚未实机验证",
                    EdgeId: edge.EdgeId,
                    MapName: edge.MapName));
            }

            var polylineDistance = CalculatePolylineDistance(edge, from, to);
            if (edge.AverageDistance > 0 && polylineDistance > 0)
            {
                var ratio = Math.Max(edge.AverageDistance, polylineDistance) /
                            Math.Max(0.001, Math.Min(edge.AverageDistance, polylineDistance));
                if (ratio > options.CostLengthRatioMaximum)
                {
                    issues.Add(new RouteGraphQualityIssue(
                        RouteGraphQualityIssueCode.CostLengthMismatch,
                        RouteGraphQualitySeverity.Warning,
                        $"边 {edge.EdgeId} 的成本距离与折线长度不一致（{ratio:F1}x）",
                        EdgeId: edge.EdgeId,
                        MapName: edge.MapName));
                }
            }
        }

        foreach (var duplicate in graph.Edges.GroupBy(
                     edge => string.Join('|', edge.MapName, edge.FromNodeId, edge.ToNodeId, edge.MoveMode),
                     StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var edge in duplicate.Skip(1))
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.DuplicateEdge,
                    RouteGraphQualitySeverity.Warning,
                    $"边 {edge.EdgeId} 与 {duplicate.First().EdgeId} 重复",
                    EdgeId: edge.EdgeId,
                    MapName: edge.MapName));
            }
        }

        foreach (var isolated in degree.Where(item => item.Value == 0))
        {
            var node = nodes[isolated.Key];
            issues.Add(new RouteGraphQualityIssue(
                RouteGraphQualityIssueCode.IsolatedNode,
                RouteGraphQualitySeverity.Warning,
                $"节点 {node.NodeId} 没有任何连接",
                node.NodeId,
                MapName: node.MapName));
        }

        AddComponentIssues(graph, nodes, options, issues);
        AddLayerCollisionIssues(graph, options, issues);
        AddTeleportIssues(graph, teleports, options, issues);
        return issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code)
            .ThenBy(issue => issue.MapName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.EdgeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddComponentIssues(
        RouteNavigationGraph graph,
        IReadOnlyDictionary<string, RouteNavigationNode> nodes,
        RouteGraphQualityOptions options,
        ICollection<RouteGraphQualityIssue> issues)
    {
        var adjacency = nodes.Keys.ToDictionary(key => key, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            if (adjacency.TryGetValue(edge.FromNodeId, out var from) && adjacency.ContainsKey(edge.ToNodeId))
            {
                from.Add(edge.ToNodeId);
                adjacency[edge.ToNodeId].Add(edge.FromNodeId);
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in adjacency.Keys)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var component = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);
                foreach (var next in adjacency[current].Where(visited.Add))
                {
                    queue.Enqueue(next);
                }
            }

            if (component.Count <= options.SmallComponentNodeCount)
            {
                var node = nodes[component[0]];
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.SmallComponent,
                    RouteGraphQualitySeverity.Info,
                    $"连通分量仅包含 {component.Count} 个节点",
                    node.NodeId,
                    MapName: node.MapName));
            }
        }
    }

    private static void AddLayerCollisionIssues(
        RouteNavigationGraph graph,
        RouteGraphQualityOptions options,
        ICollection<RouteGraphQualityIssue> issues)
    {
        var bucketSize = Math.Max(0.1, options.SameCoordinateTolerance);
        foreach (var mapGroup in graph.Nodes.GroupBy(node => RouteGraphGeometry.NormalizeMapName(node.MapName)))
        {
            var buckets = new Dictionary<(int X, int Y), List<RouteNavigationNode>>();
            foreach (var node in mapGroup)
            {
                var cell = ((int)Math.Floor(node.X / bucketSize), (int)Math.Floor(node.Y / bucketSize));
                for (var x = cell.Item1 - 1; x <= cell.Item1 + 1; x++)
                {
                    for (var y = cell.Item2 - 1; y <= cell.Item2 + 1; y++)
                    {
                        if (!buckets.TryGetValue((x, y), out var candidates))
                        {
                            continue;
                        }

                        foreach (var candidate in candidates)
                        {
                            if (string.Equals(candidate.LayerId, node.LayerId, StringComparison.OrdinalIgnoreCase) ||
                                RouteGraphGeometry.Distance(
                                    new RouteGraphPoint(candidate.X, candidate.Y),
                                    new RouteGraphPoint(node.X, node.Y)) > options.SameCoordinateTolerance)
                            {
                                continue;
                            }

                            issues.Add(new RouteGraphQualityIssue(
                                RouteGraphQualityIssueCode.SameCoordinateDifferentLayer,
                                RouteGraphQualitySeverity.Info,
                                $"节点 {candidate.NodeId} 与 {node.NodeId} 同坐标不同层",
                                candidate.NodeId,
                                MapName: candidate.MapName));
                        }
                    }
                }

                if (!buckets.TryGetValue(cell, out var bucket))
                {
                    bucket = [];
                    buckets[cell] = bucket;
                }
                bucket.Add(node);
            }
        }
    }

    private static void AddTeleportIssues(
        RouteNavigationGraph graph,
        IReadOnlyList<RouteGraphTeleportEntry> teleports,
        RouteGraphQualityOptions options,
        ICollection<RouteGraphQualityIssue> issues)
    {
        var entriesByAnchor = graph.Nodes
            .SelectMany(node => node.AnchorIds.Select(anchorId => new { anchorId, node }))
            .GroupBy(item => item.anchorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.node).ToList(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var teleport in teleports)
        {
            if (!entriesByAnchor.TryGetValue(teleport.AnchorId, out var entries) || entries.Count == 0)
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.TeleportWithoutEntry,
                    RouteGraphQualitySeverity.Warning,
                    $"传送点 {teleport.Name} 没有入口节点",
                    MapName: teleport.MapName));
                continue;
            }

            var distance = entries.Min(node => RouteGraphGeometry.Distance(
                new RouteGraphPoint(node.X, node.Y), teleport.SpawnImagePoint));
            if (distance > options.TeleportEntryMaximumDistance)
            {
                issues.Add(new RouteGraphQualityIssue(
                    RouteGraphQualityIssueCode.TeleportEntryTooFar,
                    RouteGraphQualitySeverity.Warning,
                    $"传送点 {teleport.Name} 的入口距离落地点 {distance:F1}",
                    entries[0].NodeId,
                    MapName: teleport.MapName));
            }
        }
    }

    private static double CalculatePolylineDistance(
        RouteNavigationEdge edge,
        RouteNavigationNode from,
        RouteNavigationNode to)
    {
        var points = edge.Points.Count >= 2
            ? edge.Points.Select(point => new RouteGraphPoint(point.X, point.Y)).ToList()
            : [new RouteGraphPoint(from.X, from.Y), new RouteGraphPoint(to.X, to.Y)];
        double distance = 0;
        for (var index = 1; index < points.Count; index++)
        {
            distance += RouteGraphGeometry.Distance(points[index - 1], points[index]);
        }

        return distance;
    }
}
