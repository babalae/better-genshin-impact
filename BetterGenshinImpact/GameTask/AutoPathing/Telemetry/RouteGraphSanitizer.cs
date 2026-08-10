using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteGraphSanitizationOptions
{
    /// <summary>历史路线相邻点超过该游戏距离时视为缺失传送/坏数据，不进入通用路网。</summary>
    public double MaximumImportedStraightEdgeGameDistance { get; init; } = 300;
}

public static class RouteGraphSanitizer
{
    public static int RemoveImpossibleImportedEdges(
        RouteNavigationGraph graph,
        RouteGraphSanitizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new RouteGraphSanitizationOptions();
        if (options.MaximumImportedStraightEdgeGameDistance <= 0)
        {
            return 0;
        }

        var nodes = graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeId))
            .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var removed = graph.Edges.RemoveAll(edge => IsImpossibleImportedEdge(edge, nodes, options));

        if (removed > 0)
        {
            var connected = graph.Edges
                .SelectMany(edge => new[] { edge.FromNodeId, edge.ToNodeId })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            graph.Nodes.RemoveAll(node =>
                !connected.Contains(node.NodeId) &&
                node.AnchorIds.Count == 0 &&
                node.ResourceIds.Count == 0 &&
                node.ResourceLabelIds.Count == 0);
        }

        return removed;
    }

    public static bool IsExcessiveGameDistance(
        string mapName,
        RouteGraphPoint from,
        RouteGraphPoint to,
        double maximumGameDistance)
    {
        if (maximumGameDistance <= 0)
        {
            return false;
        }

        var imageDistance = RouteGraphGeometry.Distance(from, to);
        var imageUnitsPerGameUnit = RouteMapGeometryCatalog.TryGet(mapName, out var geometry)
            ? geometry.ImageUnitsPerGameUnit
            : 1;
        return imageDistance / Math.Max(0.001, imageUnitsPerGameUnit) > maximumGameDistance;
    }

    private static bool IsImpossibleImportedEdge(
        RouteNavigationEdge edge,
        IReadOnlyDictionary<string, RouteNavigationNode> nodes,
        RouteGraphSanitizationOptions options)
    {
        if (edge.ReviewStatus == GraphReviewStatus.Verified ||
            !string.Equals(edge.SourceKind, "pathing_task", StringComparison.OrdinalIgnoreCase) ||
            edge.Points.Count > 2 ||
            !nodes.TryGetValue(edge.FromNodeId, out var from) ||
            !nodes.TryGetValue(edge.ToNodeId, out var to))
        {
            return false;
        }

        return IsExcessiveGameDistance(
            edge.MapName,
            new RouteGraphPoint(from.X, from.Y),
            new RouteGraphPoint(to.X, to.Y),
            options.MaximumImportedStraightEdgeGameDistance);
    }
}
