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
            Action = string.IsNullOrWhiteSpace(record.Action) ? health?.Action ?? string.Empty : record.Action,
            ActionParams = string.IsNullOrWhiteSpace(record.ActionParams) ? health?.ActionParams ?? string.Empty : record.ActionParams,
            IsBidirectionalCandidate = record.IsBidirectionalForAction(health?.Action),
            IsSyntheticReverse = isSyntheticReverse,
            HealthStatus = healthStatus,
            SuccessRate = health?.SuccessRate ?? 0,
            SuccessCount = health?.SuccessCount ?? 0,
            FailureCount = health?.FailureCount ?? 0,
            Cost = Math.Round(baseCost * GetHealthPenalty(healthStatus), 2),
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

        return isSyntheticReverse
            ? points.AsEnumerable().Reverse().ToList()
            : points;
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
}

public readonly record struct RouteGraphPoint(double X, double Y);
