using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteNavigationGraphProvider
{
    private const double NodeBucketSize = 64.0;
    private readonly object _syncRoot = new();
    private readonly string _graphFilePath;
    private DateTime _loadedWriteTimeUtc;
    private RouteNavigationGraphSnapshot? _snapshot;

    public RouteNavigationGraphProvider(string? saveDir = null)
    {
        var resolvedSaveDir = string.IsNullOrWhiteSpace(saveDir)
            ? Global.Absolute(Path.Combine("User", "AutoPathing", "Routes"))
            : saveDir;
        _graphFilePath = Path.Combine(resolvedSaveDir, RouteNavigationGraphBuilder.GraphFileName);
    }

    public string GraphFilePath => _graphFilePath;

    public bool TryGetSnapshot(out RouteNavigationGraphSnapshot snapshot, bool forceReload = false)
    {
        lock (_syncRoot)
        {
            if (!File.Exists(_graphFilePath))
            {
                snapshot = _snapshot ?? RouteNavigationGraphSnapshot.Empty;
                return false;
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(_graphFilePath);
            if (!forceReload && _snapshot != null && writeTimeUtc == _loadedWriteTimeUtc)
            {
                snapshot = _snapshot;
                return !snapshot.IsEmpty;
            }

            try
            {
                var json = File.ReadAllText(_graphFilePath);
                var graph = JsonSerializer.Deserialize<RouteNavigationGraph>(json) ?? new RouteNavigationGraph();
                snapshot = new RouteNavigationGraphSnapshot(graph, NodeBucketSize);
                _snapshot = snapshot;
                _loadedWriteTimeUtc = writeTimeUtc;
                return !snapshot.IsEmpty;
            }
            catch
            {
                snapshot = _snapshot ?? RouteNavigationGraphSnapshot.Empty;
                return false;
            }
        }
    }
}

public sealed class RouteNavigationGraphSnapshot
{
    public static RouteNavigationGraphSnapshot Empty { get; } = new(new RouteNavigationGraph(), 64.0);

    private readonly double _nodeBucketSize;
    private readonly Dictionary<string, RouteNavigationNode> _nodesById;
    private readonly Dictionary<string, List<RouteNavigationNode>> _nodesByMap;
    private readonly Dictionary<string, List<RouteNavigationEdge>> _edgesByMap;
    private readonly Dictionary<string, List<RouteNavigationEdge>> _outgoingEdgesByNodeId;
    private readonly Dictionary<string, Dictionary<(int X, int Y), List<RouteNavigationNode>>> _nodeBucketsByMap;
    private readonly Dictionary<string, Dictionary<(int X, int Y), List<RouteNavigationEdge>>> _edgeBucketsByMap;
    private readonly Dictionary<string, List<RouteGraphTeleportEntry>> _teleportsByMap;
    private readonly Dictionary<string, RouteGraphTeleportEntry> _teleportsByAnchorId;

    public RouteNavigationGraphSnapshot(RouteNavigationGraph graph, double nodeBucketSize)
    {
        Graph = graph;
        Nodes = graph.Nodes ?? [];
        Edges = graph.Edges ?? [];
        _nodeBucketSize = nodeBucketSize;

        _nodesById = Nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.NodeId))
            .GroupBy(n => n.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        _nodesByMap = Nodes
            .GroupBy(n => RouteGraphGeometry.NormalizeMapName(n.MapName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        _edgesByMap = Edges
            .GroupBy(e => RouteGraphGeometry.NormalizeMapName(e.MapName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        _outgoingEdgesByNodeId = Edges
            .Where(e => !string.IsNullOrWhiteSpace(e.FromNodeId))
            .GroupBy(e => e.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        _nodeBucketsByMap = BuildNodeBuckets(Nodes, nodeBucketSize);
        _edgeBucketsByMap = BuildEdgeBuckets(Edges, nodeBucketSize, GetEdgePoints);
        Teleports = LoadTeleportEntries();
        _teleportsByMap = Teleports
            .GroupBy(t => RouteGraphGeometry.NormalizeMapName(t.MapName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        _teleportsByAnchorId = Teleports
            .GroupBy(t => t.AnchorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public RouteNavigationGraph Graph { get; }

    public IReadOnlyList<RouteNavigationNode> Nodes { get; }

    public IReadOnlyList<RouteNavigationEdge> Edges { get; }

    public IReadOnlyList<RouteGraphTeleportEntry> Teleports { get; }

    public bool IsEmpty => Nodes.Count == 0 || Edges.Count == 0;

    public RouteNavigationNode? GetNode(string nodeId)
    {
        return _nodesById.TryGetValue(nodeId, out var node) ? node : null;
    }

    public IReadOnlyList<RouteNavigationEdge> GetOutgoingEdges(string nodeId)
    {
        return _outgoingEdgesByNodeId.TryGetValue(nodeId, out var edges) ? edges : [];
    }

    public RouteGraphTeleportEntry? GetTeleportByAnchorId(string anchorId)
    {
        return _teleportsByAnchorId.TryGetValue(anchorId, out var teleport) ? teleport : null;
    }

    public IReadOnlyList<RouteNavigationNode> GetTeleportEntryNodes(string anchorId)
    {
        if (string.IsNullOrWhiteSpace(anchorId))
        {
            return [];
        }

        return Nodes
            .Where(node => node.AnchorIds.Contains(anchorId))
            .ToList();
    }

    public IReadOnlyList<RouteNavigationNode> FindResourceNodes(
        string mapName,
        string? resourceId,
        string? resourceLabelId,
        RouteGraphPoint fallbackPoint,
        int limit,
        double maxDistance = 0)
    {
        if (limit <= 0 || (string.IsNullOrWhiteSpace(resourceId) && string.IsNullOrWhiteSpace(resourceLabelId)))
        {
            return [];
        }

        var normalizedMapName = RouteGraphGeometry.NormalizeMapName(mapName);
        if (!_nodesByMap.TryGetValue(normalizedMapName, out var mapNodes))
        {
            return [];
        }

        return mapNodes
            .Where(node =>
                (!string.IsNullOrWhiteSpace(resourceId) && node.ResourceIds.Contains(resourceId)) ||
                (!string.IsNullOrWhiteSpace(resourceLabelId) && node.ResourceLabelIds.Contains(resourceLabelId)))
            .Select(node => new
            {
                Node = node,
                Distance = RouteGraphGeometry.Distance(fallbackPoint, new RouteGraphPoint(node.X, node.Y))
            })
            .Where(candidate => maxDistance <= 0 || candidate.Distance <= maxDistance)
            .OrderBy(candidate => candidate.Distance)
            .Take(limit)
            .Select(candidate => candidate.Node)
            .ToList();
    }

    public IReadOnlyList<RouteGraphTeleportCandidate> FindNearestTeleports(
        string mapName,
        RouteGraphPoint point,
        int limit,
        double maxDistance = 0)
    {
        if (limit <= 0)
        {
            return [];
        }

        var normalizedMapName = RouteGraphGeometry.NormalizeMapName(mapName);
        if (!_teleportsByMap.TryGetValue(normalizedMapName, out var teleports))
        {
            return [];
        }

        return teleports
            .Select(teleport => new RouteGraphTeleportCandidate(
                teleport,
                RouteGraphGeometry.Distance(point, teleport.SpawnImagePoint)))
            .Where(candidate => maxDistance <= 0 || candidate.Distance <= maxDistance)
            .OrderBy(candidate => candidate.Distance)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<RouteGraphNodeCandidate> FindNearestNodes(
        string mapName,
        RouteGraphPoint point,
        int limit,
        double maxDistance = 0)
    {
        if (limit <= 0)
        {
            return [];
        }

        var normalizedMapName = RouteGraphGeometry.NormalizeMapName(mapName);
        if (!_nodesByMap.TryGetValue(normalizedMapName, out var mapNodes))
        {
            return [];
        }

        IEnumerable<RouteNavigationNode> searchNodes;
        if (maxDistance > 0 && _nodeBucketsByMap.TryGetValue(normalizedMapName, out var buckets))
        {
            var centerCell = GetCell(point.X, point.Y, _nodeBucketSize);
            var cellRadius = Math.Max(0, (int)Math.Ceiling(maxDistance / _nodeBucketSize));
            var nodes = new List<RouteNavigationNode>();
            for (var dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (var dy = -cellRadius; dy <= cellRadius; dy++)
                {
                    var key = (centerCell.X + dx, centerCell.Y + dy);
                    if (buckets.TryGetValue(key, out var bucketNodes))
                    {
                        nodes.AddRange(bucketNodes);
                    }
                }
            }

            searchNodes = nodes;
        }
        else
        {
            searchNodes = mapNodes;
        }

        return searchNodes
            .Select(node => new RouteGraphNodeCandidate(
                node,
                RouteGraphGeometry.Distance(point, new RouteGraphPoint(node.X, node.Y))))
            .Where(candidate => maxDistance <= 0 || candidate.Distance <= maxDistance)
            .OrderBy(candidate => candidate.Distance)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<RouteGraphEdgeProjection> FindNearestEdges(
        string mapName,
        RouteGraphPoint point,
        int limit,
        double maxDistance)
    {
        if (limit <= 0)
        {
            return [];
        }

        var normalizedMapName = RouteGraphGeometry.NormalizeMapName(mapName);
        if (!_edgesByMap.TryGetValue(normalizedMapName, out var edges))
        {
            return [];
        }

        var searchEdges = ResolveSearchEdges(normalizedMapName, point, maxDistance, edges);

        return searchEdges
            .Select(edge => TryProjectToEdge(edge, point, out var projection) ? projection : null)
            .Where(projection => projection != null && projection.Distance <= maxDistance)
            .OrderBy(projection => projection!.Distance)
            .Take(limit)
            .Cast<RouteGraphEdgeProjection>()
            .ToList();
    }

    private IEnumerable<RouteNavigationEdge> ResolveSearchEdges(
        string normalizedMapName,
        RouteGraphPoint point,
        double maxDistance,
        IReadOnlyList<RouteNavigationEdge> fallbackEdges)
    {
        if (maxDistance <= 0 || !_edgeBucketsByMap.TryGetValue(normalizedMapName, out var buckets))
        {
            return fallbackEdges;
        }

        var centerCell = GetCell(point.X, point.Y, _nodeBucketSize);
        var cellRadius = Math.Max(0, (int)Math.Ceiling(maxDistance / _nodeBucketSize));
        var result = new Dictionary<string, RouteNavigationEdge>(StringComparer.OrdinalIgnoreCase);
        for (var dx = -cellRadius; dx <= cellRadius; dx++)
        {
            for (var dy = -cellRadius; dy <= cellRadius; dy++)
            {
                var key = (centerCell.X + dx, centerCell.Y + dy);
                if (!buckets.TryGetValue(key, out var bucketEdges))
                {
                    continue;
                }

                foreach (var edge in bucketEdges)
                {
                    result.TryAdd(edge.EdgeId, edge);
                }
            }
        }

        return result.Values;
    }

    private bool TryProjectToEdge(RouteNavigationEdge edge, RouteGraphPoint point, out RouteGraphEdgeProjection projection)
    {
        projection = null!;
        var points = GetEdgePoints(edge);
        if (points.Count < 2)
        {
            return false;
        }

        var bestDistance = double.MaxValue;
        var bestProjectedPoint = default(RouteGraphPoint);
        var bestAlongDistance = 0.0;
        var distanceFromStart = 0.0;
        var bestSegmentIndex = 0;

        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var segmentLength = RouteGraphGeometry.Distance(a, b);
            if (segmentLength <= 0)
            {
                continue;
            }

            var t = RouteGraphGeometry.ProjectRatio(point, a, b);
            var projectedPoint = new RouteGraphPoint(
                a.X + ((b.X - a.X) * t),
                a.Y + ((b.Y - a.Y) * t));
            var distance = RouteGraphGeometry.Distance(point, projectedPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestProjectedPoint = projectedPoint;
                bestAlongDistance = distanceFromStart + (segmentLength * t);
                bestSegmentIndex = i - 1;
            }

            distanceFromStart += segmentLength;
        }

        if (bestDistance == double.MaxValue)
        {
            return false;
        }

        projection = new RouteGraphEdgeProjection(
            edge,
            bestProjectedPoint,
            bestDistance,
            bestAlongDistance,
            Math.Max(0, distanceFromStart - bestAlongDistance),
            bestSegmentIndex);
        return true;
    }

    private List<RouteGraphPoint> GetEdgePoints(RouteNavigationEdge edge)
    {
        if (edge.Points is { Count: >= 2 })
        {
            return edge.Points
                .Select(point => new RouteGraphPoint(point.X, point.Y))
                .ToList();
        }

        if (_nodesById.TryGetValue(edge.FromNodeId, out var fromNode) &&
            _nodesById.TryGetValue(edge.ToNodeId, out var toNode))
        {
            return
            [
                new RouteGraphPoint(fromNode.X, fromNode.Y),
                new RouteGraphPoint(toNode.X, toNode.Y)
            ];
        }

        return [];
    }

    private static Dictionary<string, Dictionary<(int X, int Y), List<RouteNavigationNode>>> BuildNodeBuckets(
        IReadOnlyList<RouteNavigationNode> nodes,
        double bucketSize)
    {
        var result = new Dictionary<string, Dictionary<(int X, int Y), List<RouteNavigationNode>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var mapName = RouteGraphGeometry.NormalizeMapName(node.MapName);
            if (!result.TryGetValue(mapName, out var mapBuckets))
            {
                mapBuckets = new Dictionary<(int X, int Y), List<RouteNavigationNode>>();
                result[mapName] = mapBuckets;
            }

            var cell = GetCell(node.X, node.Y, bucketSize);
            if (!mapBuckets.TryGetValue(cell, out var bucketNodes))
            {
                bucketNodes = [];
                mapBuckets[cell] = bucketNodes;
            }

            bucketNodes.Add(node);
        }

        return result;
    }

    private static Dictionary<string, Dictionary<(int X, int Y), List<RouteNavigationEdge>>> BuildEdgeBuckets(
        IReadOnlyList<RouteNavigationEdge> edges,
        double bucketSize,
        Func<RouteNavigationEdge, List<RouteGraphPoint>> edgePointResolver)
    {
        var result = new Dictionary<string, Dictionary<(int X, int Y), List<RouteNavigationEdge>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            var points = edgePointResolver(edge);
            if (points.Count == 0)
            {
                continue;
            }

            var mapName = RouteGraphGeometry.NormalizeMapName(edge.MapName);
            if (!result.TryGetValue(mapName, out var mapBuckets))
            {
                mapBuckets = new Dictionary<(int X, int Y), List<RouteNavigationEdge>>();
                result[mapName] = mapBuckets;
            }

            var minX = points.Min(p => p.X);
            var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxY = points.Max(p => p.Y);
            var minCell = GetCell(minX, minY, bucketSize);
            var maxCell = GetCell(maxX, maxY, bucketSize);

            for (var x = minCell.X; x <= maxCell.X; x++)
            {
                for (var y = minCell.Y; y <= maxCell.Y; y++)
                {
                    var key = (x, y);
                    if (!mapBuckets.TryGetValue(key, out var bucketEdges))
                    {
                        bucketEdges = [];
                        mapBuckets[key] = bucketEdges;
                    }

                    bucketEdges.Add(edge);
                }
            }
        }

        return result;
    }

    private static List<RouteGraphTeleportEntry> LoadTeleportEntries()
    {
        var result = new List<RouteGraphTeleportEntry>();
        try
        {
            foreach (var scene in MapLazyAssets.Instance.ScenesDic.Values)
            {
                var mapName = RouteGraphGeometry.NormalizeMapName(scene.MapName);
                var map = MapManager.GetMap(mapName, string.Empty);
                if (map == null)
                {
                    continue;
                }

                foreach (var tp in scene.Points.Where(IsTeleportLike))
                {
                    var imagePoint = map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)tp.X, (float)tp.Y));
                    var spawnGamePoint = ResolveTeleportSpawnPoint(tp);
                    var spawnImagePoint = map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)spawnGamePoint.X, (float)spawnGamePoint.Y));
                    result.Add(new RouteGraphTeleportEntry(
                        mapName,
                        CreateTeleportAnchorId(tp),
                        tp.Id,
                        tp.Name ?? string.Empty,
                        tp.Type ?? string.Empty,
                        tp.X,
                        tp.Y,
                        imagePoint.X,
                        imagePoint.Y,
                        spawnGamePoint.X,
                        spawnGamePoint.Y,
                        spawnImagePoint.X,
                        spawnImagePoint.Y));
                }
            }
        }
        catch
        {
            return [];
        }

        return result;
    }

    private static bool IsTeleportLike(GiTpPosition tp)
    {
        if (string.IsNullOrWhiteSpace(tp.Type))
        {
            return false;
        }

        return tp.Type.Contains("Teleport", StringComparison.OrdinalIgnoreCase) ||
               tp.Type.Contains("Domain", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tp.Type, "Goddess", StringComparison.OrdinalIgnoreCase);
    }

    private static RouteGraphPoint ResolveTeleportSpawnPoint(GiTpPosition tp)
    {
        if (tp.TranPosition is { Length: >= 3 } &&
            (tp.TranPosition[0] != 0 || tp.TranPosition[2] != 0))
        {
            return new RouteGraphPoint(tp.TranX, tp.TranY);
        }

        return new RouteGraphPoint(tp.X, tp.Y);
    }

    private static string CreateTeleportAnchorId(GiTpPosition tp)
    {
        return $"TP_{Math.Round(tp.X)}_{Math.Round(tp.Y)}";
    }

    private static (int X, int Y) GetCell(double x, double y, double bucketSize)
    {
        return ((int)Math.Floor(x / bucketSize), (int)Math.Floor(y / bucketSize));
    }
}

public sealed record RouteGraphNodeCandidate(RouteNavigationNode Node, double Distance);

public sealed record RouteGraphTeleportCandidate(RouteGraphTeleportEntry Teleport, double Distance);

public sealed record RouteGraphTeleportEntry(
    string MapName,
    string AnchorId,
    string Id,
    string Name,
    string Type,
    double GameX,
    double GameY,
    double ImageX,
    double ImageY,
    double SpawnGameX,
    double SpawnGameY,
    double SpawnImageX,
    double SpawnImageY)
{
    public RouteGraphPoint ImagePoint => new(ImageX, ImageY);

    public RouteGraphPoint SpawnImagePoint => new(SpawnImageX, SpawnImageY);
}

public sealed record RouteGraphEdgeProjection(
    RouteNavigationEdge Edge,
    RouteGraphPoint ProjectedPoint,
    double Distance,
    double DistanceFromEdgeStart,
    double DistanceToEdgeEnd,
    int SegmentIndex);

internal static class RouteGraphGeometry
{
    public static string NormalizeMapName(string? mapName)
    {
        return string.IsNullOrWhiteSpace(mapName) ? "Teyvat" : mapName;
    }

    public static double Distance(RouteGraphPoint a, RouteGraphPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static double PolylineDistance(IReadOnlyList<RouteGraphPoint> points)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        double distance = 0;
        for (var i = 1; i < points.Count; i++)
        {
            distance += Distance(points[i - 1], points[i]);
        }

        return distance;
    }

    public static double ProjectRatio(RouteGraphPoint point, RouteGraphPoint a, RouteGraphPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 0)
        {
            return 0;
        }

        var t = (((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / lengthSquared;
        return Math.Clamp(t, 0, 1);
    }
}
