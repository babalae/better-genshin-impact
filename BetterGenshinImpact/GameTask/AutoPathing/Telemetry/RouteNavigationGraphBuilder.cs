using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteNavigationGraphBuilder
{
    public const string GraphFileName = "route_navigation_graph.json";
    private static readonly Regex PointRegex = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    private readonly object _syncRoot = new();
    private readonly string _saveDir;
    private readonly string _graphFilePath;
    private IReadOnlyCollection<RouteHealthEntry> _pendingHealthEntries = [];
    private int _isBuilding;
    private volatile bool _hasPendingBuild;

    public RouteNavigationGraphBuilder(string saveDir)
    {
        _saveDir = saveDir;
        Directory.CreateDirectory(_saveDir);
        _graphFilePath = Path.Combine(_saveDir, GraphFileName);
    }

    public void ScheduleBuild(IReadOnlyCollection<RouteHealthEntry> healthEntries)
    {
        lock (_syncRoot)
        {
            _pendingHealthEntries = healthEntries.Select(e => e.Clone()).ToList();
            _hasPendingBuild = true;
        }

        if (Interlocked.CompareExchange(ref _isBuilding, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(BuildLoop);
    }

    public void BuildNow(IReadOnlyCollection<RouteHealthEntry> healthEntries)
    {
        BuildGraph(healthEntries.Select(e => e.Clone()).ToList());
    }

    private void BuildLoop()
    {
        try
        {
            do
            {
                IReadOnlyCollection<RouteHealthEntry> healthEntries;
                lock (_syncRoot)
                {
                    _hasPendingBuild = false;
                    healthEntries = _pendingHealthEntries;
                }

                BuildGraph(healthEntries);
            }
            while (_hasPendingBuild);
        }
        finally
        {
            Interlocked.Exchange(ref _isBuilding, 0);
            if (_hasPendingBuild && Interlocked.CompareExchange(ref _isBuilding, 1, 0) == 0)
            {
                _ = Task.Run(BuildLoop);
            }
        }
    }

    private void BuildGraph(IReadOnlyCollection<RouteHealthEntry> healthEntries)
    {
        try
        {
            var healthBySegmentId = healthEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.SegmentId))
                .ToDictionary(e => e.SegmentId, StringComparer.OrdinalIgnoreCase);

            var records = LoadTelemetryRecords();
            var representativeRecords = records
                .Where(r => r.Points is { Count: >= 2 })
                .GroupBy(GetRecordSegmentId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(r => r.Timestamp).First())
                .ToList();

            var nodes = new Dictionary<string, RouteNavigationNode>(StringComparer.OrdinalIgnoreCase);
            var edges = new List<RouteNavigationEdge>();

            foreach (var record in representativeRecords)
            {
                var segmentId = GetRecordSegmentId(record);
                var segmentPoints = ResolveSegmentEndpoints(record);
                if (segmentPoints == null)
                {
                    continue;
                }

                var (start, end) = segmentPoints.Value;
                var fromNode = GetOrAddNode(nodes, record.MapName, start.X, start.Y);
                var toNode = GetOrAddNode(nodes, record.MapName, end.X, end.Y);
                var health = healthBySegmentId.TryGetValue(segmentId, out var entry) ? entry : null;

                fromNode.AnchorIds.Add(record.AnchorId);
                if (!string.IsNullOrWhiteSpace(record.TargetResourceId))
                {
                    toNode.ResourceIds.Add(record.TargetResourceId);
                }
                if (!string.IsNullOrWhiteSpace(record.TargetResourceLabelId))
                {
                    toNode.ResourceLabelIds.Add(record.TargetResourceLabelId);
                }

                var edge = RouteNavigationEdge.FromRecord(record, segmentId, fromNode.NodeId, toNode.NodeId, health);
                edges.Add(edge);

                if (edge.IsBidirectionalCandidate)
                {
                    edges.Add(RouteNavigationEdge.FromRecord(
                        record,
                        segmentId,
                        toNode.NodeId,
                        fromNode.NodeId,
                        health,
                        isSyntheticReverse: true));
                }
            }

            var graph = new RouteNavigationGraph
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Nodes = nodes.Values
                    .OrderBy(n => n.MapName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.X)
                    .ThenBy(n => n.Y)
                    .ToList(),
                Edges = edges
                    .OrderBy(e => e.MapName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.EdgeId, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            WriteGraph(graph);
        }
        catch
        {
            // Graph data is opportunistic telemetry output and must not affect route execution.
        }
    }

    private List<RouteTelemetryRecord> LoadTelemetryRecords()
    {
        var records = new List<RouteTelemetryRecord>();
        foreach (var filePath in Directory.EnumerateFiles(_saveDir, "*_Telemetry.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var fileRecords = JsonSerializer.Deserialize<List<RouteTelemetryRecord>>(json) ?? [];
                foreach (var record in fileRecords)
                {
                    record.SourceFileName = Path.GetFileName(filePath);
                }

                records.AddRange(fileRecords);
            }
            catch
            {
                // Ignore corrupted telemetry files; raw telemetry keeps its own backups.
            }
        }

        return records;
    }

    private static string GetRecordSegmentId(RouteTelemetryRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.SegmentId))
        {
            return record.SegmentId;
        }

        var raw = string.Join('|',
            record.MapName,
            record.AnchorId,
            record.SegmentKey,
            record.MoveMode,
            record.TargetResourceId,
            record.TargetResourceLabelId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "legacy_seg_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static (RouteGraphPoint Start, RouteGraphPoint End)? ResolveSegmentEndpoints(RouteTelemetryRecord record)
    {
        if (TryParseSegmentKey(record.SegmentKey, out var start, out var end))
        {
            return (start, end);
        }

        if (record.Points is not { Count: >= 2 })
        {
            return null;
        }

        var first = record.Points[0];
        var last = record.Points[^1];
        return (new RouteGraphPoint(first.X, first.Y), new RouteGraphPoint(last.X, last.Y));
    }

    private static bool TryParseSegmentKey(string segmentKey, out RouteGraphPoint start, out RouteGraphPoint end)
    {
        start = default;
        end = default;

        if (string.IsNullOrWhiteSpace(segmentKey))
        {
            return false;
        }

        var matches = PointRegex.Matches(segmentKey);
        if (matches.Count < 4)
        {
            return false;
        }

        if (!TryParseInvariant(matches[0].Value, out var startX) ||
            !TryParseInvariant(matches[1].Value, out var startY) ||
            !TryParseInvariant(matches[2].Value, out var endX) ||
            !TryParseInvariant(matches[3].Value, out var endY))
        {
            return false;
        }

        start = new RouteGraphPoint(startX, startY);
        end = new RouteGraphPoint(endX, endY);
        return true;
    }

    private static bool TryParseInvariant(string value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static RouteNavigationNode GetOrAddNode(Dictionary<string, RouteNavigationNode> nodes, string mapName, double x, double y)
    {
        var nodeId = RouteNavigationNode.CreateNodeId(mapName, x, y);
        if (nodes.TryGetValue(nodeId, out var node))
        {
            return node;
        }

        node = new RouteNavigationNode
        {
            NodeId = nodeId,
            MapName = string.IsNullOrWhiteSpace(mapName) ? "Teyvat" : mapName,
            X = Math.Round(x, 1),
            Y = Math.Round(y, 1)
        };
        nodes[nodeId] = node;
        return node;
    }

    private void WriteGraph(RouteNavigationGraph graph)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var tempPath = _graphFilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(graph, options));
        File.Copy(tempPath, _graphFilePath, true);
        File.Delete(tempPath);
    }
}

public sealed class RouteNavigationGraph
{
    public int SchemaVersion { get; set; } = 1;

    public DateTime GeneratedAtUtc { get; set; }

    public List<RouteNavigationNode> Nodes { get; set; } = [];

    public List<RouteNavigationEdge> Edges { get; set; } = [];
}

public sealed class RouteNavigationNode
{
    public string NodeId { get; set; } = string.Empty;

    public string MapName { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public HashSet<string> AnchorIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ResourceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ResourceLabelIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string CreateNodeId(string mapName, double x, double y)
    {
        var normalizedMapName = string.IsNullOrWhiteSpace(mapName) ? "Teyvat" : mapName;
        var raw = string.Create(CultureInfo.InvariantCulture, $"{normalizedMapName}|{Math.Round(x, 1):F1}|{Math.Round(y, 1):F1}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "node_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

public sealed class RouteNavigationEdge
{
    public string EdgeId { get; set; } = string.Empty;

    public string SegmentId { get; set; } = string.Empty;

    public string FromNodeId { get; set; } = string.Empty;

    public string ToNodeId { get; set; } = string.Empty;

    public string MapName { get; set; } = string.Empty;

    public string AnchorId { get; set; } = string.Empty;

    public string SegmentKey { get; set; } = string.Empty;

    public string MoveMode { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string ActionParams { get; set; } = string.Empty;

    public bool IsBidirectionalCandidate { get; set; }

    public bool IsSyntheticReverse { get; set; }

    public string HealthStatus { get; set; } = RouteHealthStatus.Unknown;

    public double SuccessRate { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public double Cost { get; set; }

    public double AverageDistance { get; set; }

    public double AverageDurationMs { get; set; }

    public string LastFailureReason { get; set; } = string.Empty;

    public string SourceRecordId { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string TargetResourceId { get; set; } = string.Empty;

    public string TargetResourceLabelId { get; set; } = string.Empty;

    public List<string> PickedItems { get; set; } = [];

    public List<TelemetryPoint2D> Points { get; set; } = [];

    public static RouteNavigationEdge FromRecord(
        RouteTelemetryRecord record,
        string segmentId,
        string fromNodeId,
        string toNodeId,
        RouteHealthEntry? health,
        bool isSyntheticReverse = false)
    {
        var recordDistance = record.RouteDistance > 0 ? record.RouteDistance : CalculatePointDistance(record.Points);
        var averageDistance = health?.AverageDistance > 0 ? health.AverageDistance : recordDistance;
        var averageDurationMs = health?.AverageDurationMs > 0 ? health.AverageDurationMs : record.DurationMs;
        var healthStatus = health?.Status ?? RouteHealthStatus.Unknown;
        var baseCost = averageDurationMs > 0 ? averageDurationMs / 1000.0 : averageDistance;
        var action = string.IsNullOrWhiteSpace(record.Action) ? health?.Action ?? string.Empty : record.Action;
        var actionParams = string.IsNullOrWhiteSpace(record.ActionParams) ? health?.ActionParams ?? string.Empty : record.ActionParams;
        var cost = baseCost
            * GetHealthPenalty(healthStatus)
            * GetSamplePenalty(health)
            * GetFailureRatePenalty(health)
            * GetMoveModePenalty(record.MoveMode)
            * GetActionPenalty(action)
            * GetStalePenalty(health);

        return new RouteNavigationEdge
        {
            EdgeId = isSyntheticReverse ? $"edge_{segmentId}_reverse" : $"edge_{segmentId}",
            SegmentId = segmentId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            MapName = record.MapName,
            AnchorId = record.AnchorId,
            SegmentKey = record.SegmentKey,
            MoveMode = record.MoveMode,
            Action = action,
            ActionParams = actionParams,
            IsBidirectionalCandidate = record.IsBidirectionalForAction(health?.Action),
            IsSyntheticReverse = isSyntheticReverse,
            HealthStatus = healthStatus,
            SuccessRate = health?.SuccessRate ?? 0,
            SuccessCount = health?.SuccessCount ?? 0,
            FailureCount = health?.FailureCount ?? 0,
            Cost = Math.Round(cost, 2),
            AverageDistance = Math.Round(averageDistance, 2),
            AverageDurationMs = Math.Round(averageDurationMs, 0),
            LastFailureReason = health?.LastFailureReason ?? string.Empty,
            SourceRecordId = record.RecordId,
            SourceFileName = record.SourceFileName,
            TargetResourceId = isSyntheticReverse ? string.Empty : record.TargetResourceId,
            TargetResourceLabelId = isSyntheticReverse ? string.Empty : record.TargetResourceLabelId,
            PickedItems = (record.PickedItems ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Points = ResolveEdgePoints(record.Points, isSyntheticReverse)
        };
    }

    private static List<TelemetryPoint2D> ResolveEdgePoints(List<TelemetryPoint2D>? points, bool isSyntheticReverse)
    {
        if (points == null)
        {
            return [];
        }

        var resolvedPoints = isSyntheticReverse
            ? points.AsEnumerable().Reverse().ToList()
            : points;

        return RoutePolylineSimplifier.Simplify(resolvedPoints);
    }

    private static double CalculatePointDistance(List<TelemetryPoint2D>? points)
    {
        if (points is not { Count: >= 2 })
        {
            return 0;
        }

        double distance = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            distance += Math.Sqrt(dx * dx + dy * dy);
        }

        return distance;
    }

    private static double GetHealthPenalty(string healthStatus)
    {
        return healthStatus switch
        {
            RouteHealthStatus.Verified => 1.0,
            RouteHealthStatus.Risky => 2.0,
            RouteHealthStatus.Disabled => 1000.0,
            _ => 3.0
        };
    }

    private static double GetSamplePenalty(RouteHealthEntry? health)
    {
        if (health == null)
        {
            return 1.15;
        }

        var total = health.SuccessCount + health.FailureCount;
        if (total <= 1)
        {
            return 1.2;
        }

        return total < 3 ? 1.1 : 1.0;
    }

    private static double GetFailureRatePenalty(RouteHealthEntry? health)
    {
        if (health == null)
        {
            return 1.0;
        }

        var total = health.SuccessCount + health.FailureCount;
        if (total == 0)
        {
            return 1.0;
        }

        var failureRate = (double)health.FailureCount / total;
        return 1.0 + Math.Min(2.0, failureRate * 2.0);
    }

    private static double GetMoveModePenalty(string? moveMode)
    {
        if (string.IsNullOrWhiteSpace(moveMode))
        {
            return 1.0;
        }

        if (moveMode.Contains("fly", StringComparison.OrdinalIgnoreCase) ||
            moveMode.Contains("climb", StringComparison.OrdinalIgnoreCase) ||
            moveMode.Contains("jump", StringComparison.OrdinalIgnoreCase))
        {
            return 1.35;
        }

        return 1.0;
    }

    private static double GetActionPenalty(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return 1.0;
        }

        // 带交互/战斗语义的边可以复用，但不应在普通全图导航中被过度偏好。
        return 1.25;
    }

    private static double GetStalePenalty(RouteHealthEntry? health)
    {
        if (health?.LastSuccessUtc == null)
        {
            return 1.0;
        }

        var ageDays = (DateTime.UtcNow - health.LastSuccessUtc.Value).TotalDays;
        if (ageDays <= 14)
        {
            return 1.0;
        }

        return Math.Min(1.5, 1.0 + ((ageDays - 14) / 180.0));
    }
}

internal static class RoutePolylineSimplifier
{
    private const int MinPointCount = 20;
    private const double Tolerance = 2.0;

    public static List<TelemetryPoint2D> Simplify(IReadOnlyList<TelemetryPoint2D> points)
    {
        if (points.Count < 2)
        {
            return [];
        }

        if (points.Count < MinPointCount || Tolerance <= 0)
        {
            return points.Select(ClonePoint).ToList();
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifySection(points, 0, points.Count - 1, Tolerance * Tolerance, keep);

        var result = new List<TelemetryPoint2D>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(ClonePoint(points[i]));
            }
        }

        return result;
    }

    private static void SimplifySection(IReadOnlyList<TelemetryPoint2D> points, int start, int end, double toleranceSquared, bool[] keep)
    {
        if (end <= start + 1)
        {
            return;
        }

        var maxDistanceSquared = 0.0;
        var index = -1;
        for (var i = start + 1; i < end; i++)
        {
            var distanceSquared = PerpendicularDistanceSquared(points[i], points[start], points[end]);
            if (distanceSquared > maxDistanceSquared)
            {
                maxDistanceSquared = distanceSquared;
                index = i;
            }
        }

        if (index < 0 || maxDistanceSquared <= toleranceSquared)
        {
            return;
        }

        keep[index] = true;
        SimplifySection(points, start, index, toleranceSquared, keep);
        SimplifySection(points, index, end, toleranceSquared, keep);
    }

    private static double PerpendicularDistanceSquared(TelemetryPoint2D point, TelemetryPoint2D lineStart, TelemetryPoint2D lineEnd)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 0)
        {
            var px = point.X - lineStart.X;
            var py = point.Y - lineStart.Y;
            return (px * px) + (py * py);
        }

        var t = (((point.X - lineStart.X) * dx) + ((point.Y - lineStart.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        var projectedX = lineStart.X + (t * dx);
        var projectedY = lineStart.Y + (t * dy);
        var distanceX = point.X - projectedX;
        var distanceY = point.Y - projectedY;
        return (distanceX * distanceX) + (distanceY * distanceY);
    }

    private static TelemetryPoint2D ClonePoint(TelemetryPoint2D point)
    {
        return new TelemetryPoint2D { X = point.X, Y = point.Y };
    }
}

public readonly record struct RouteGraphPoint(double X, double Y);
