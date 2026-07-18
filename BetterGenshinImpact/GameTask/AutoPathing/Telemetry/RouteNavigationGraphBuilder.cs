using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteNavigationGraphBuilder
{
    public const string GraphFileName = "route_navigation_graph.generated.json";
    public const string LegacyGraphFileName = "route_navigation_graph.json";
    private static readonly Regex PointRegex = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    private readonly object _syncRoot = new();
    private readonly string _saveDir;
    private readonly string _graphFilePath;
    private readonly IRouteCoordinateConverter _coordinateConverter;
    private IReadOnlyCollection<RouteHealthEntry> _pendingHealthEntries = [];
    private int _isBuilding;
    private volatile bool _hasPendingBuild;

    public RouteNavigationGraphBuilder(
        string saveDir,
        IRouteCoordinateConverter? coordinateConverter = null)
    {
        _saveDir = saveDir;
        Directory.CreateDirectory(_saveDir);
        _graphFilePath = Path.Combine(_saveDir, GraphFileName);
        _coordinateConverter = coordinateConverter ?? RouteNavigationCoordinateService.Instance;
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

    public RouteNavigationBuildResult BuildNow(IReadOnlyCollection<RouteHealthEntry> healthEntries)
    {
        return BuildGraph(new RouteNavigationBuildRequest
        {
            HealthEntries = healthEntries.Select(e => e.Clone()).ToList(),
            IncludeTelemetry = true,
            NodeSnapDistance = 0
        });
    }

    public RouteNavigationBuildResult BuildNow(RouteNavigationBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BuildGraph(request);
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

                BuildGraph(new RouteNavigationBuildRequest
                {
                    HealthEntries = healthEntries,
                    IncludeTelemetry = true,
                    NodeSnapDistance = 0
                });
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

    private RouteNavigationBuildResult BuildGraph(RouteNavigationBuildRequest request)
    {
        PathingTaskRouteImportResult? importResult = null;
        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            var healthBySegmentId = request.HealthEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.SegmentId))
                .ToDictionary(e => e.SegmentId, StringComparer.OrdinalIgnoreCase);

            var records = request.IncludeTelemetry ? LoadTelemetryRecords() : [];
            var representativeRecords = records
                .Where(r => r.Points is { Count: >= 2 })
                .GroupBy(GetRecordSegmentId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(r => r.Timestamp).First())
                .ToList();

            var nodes = new Dictionary<string, RouteNavigationNode>(StringComparer.OrdinalIgnoreCase);
            var edges = new List<RouteNavigationEdge>();
            var nodeIndex = new RouteGraphNodeSnapIndex(nodes, request.NodeSnapDistance);

            foreach (var record in representativeRecords)
            {
                request.CancellationToken.ThrowIfCancellationRequested();
                var segmentId = GetRecordSegmentId(record);
                var segmentPoints = ResolveSegmentEndpoints(record);
                if (segmentPoints == null)
                {
                    continue;
                }

                var (start, end) = segmentPoints.Value;
                var fromNode = nodeIndex.GetOrAdd(record.MapName, start);
                var toNode = nodeIndex.GetOrAdd(record.MapName, end, fromNode.NodeId);
                if (string.Equals(fromNode.NodeId, toNode.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
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

            var importedEdgeCount = 0;
            if (request.PathingTaskDirectories.Count > 0)
            {
                importResult = new PathingTaskRouteImporter(_coordinateConverter).Import(
                    request.PathingTaskDirectories,
                    request.CancellationToken,
                    request.MaximumImportedStraightEdgeGameDistance);
                foreach (var segment in importResult.Segments)
                {
                    request.CancellationToken.ThrowIfCancellationRequested();
                    var fromNode = nodeIndex.GetOrAdd(segment.MapName, segment.Start);
                    var toNode = nodeIndex.GetOrAdd(segment.MapName, segment.End, fromNode.NodeId);
                    if (string.Equals(fromNode.NodeId, toNode.NodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(segment.AnchorId))
                    {
                        fromNode.AnchorIds.Add(segment.AnchorId);
                    }

                    var segmentId = CreateImportedSegmentId(segment);
                    edges.Add(RouteNavigationEdge.FromSourceSegment(
                        segment,
                        segmentId,
                        fromNode.NodeId,
                        toNode.NodeId));
                    importedEdgeCount++;
                    if (segment.IsBidirectionalCandidate)
                    {
                        edges.Add(RouteNavigationEdge.FromSourceSegment(
                            segment,
                            segmentId,
                            toNode.NodeId,
                            fromNode.NodeId,
                        isSyntheticReverse: true));
                    }
                }

                if (importedEdgeCount == 0)
                {
                    return RouteNavigationBuildResult.Failed(
                        _graphFilePath,
                        "所选目录没有产生任何可用历史路线边，已保留现有路网文件。",
                        importResult.Report);
                }
            }

            edges = MergeDuplicateEdges(edges);
            if (edges.Count == 0)
            {
                return RouteNavigationBuildResult.Failed(
                    _graphFilePath,
                    "没有生成任何可用路网边，已保留现有路网文件。",
                    importResult?.Report);
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
            graph.GraphId = RouteNavigationGraphIdentity.Compute(graph);

            WriteGraph(graph);
            return RouteNavigationBuildResult.Succeeded(
                _graphFilePath,
                graph,
                importResult?.Report);
        }
        catch (Exception ex)
        {
            // Preserve the previous graph and return a user-visible failure to the caller.
            return RouteNavigationBuildResult.Failed(
                _graphFilePath,
                ex.Message,
                importResult?.Report);
        }
    }

    private static List<RouteNavigationEdge> MergeDuplicateEdges(
        IEnumerable<RouteNavigationEdge> edges)
    {
        return edges
            .GroupBy(
                edge => string.Join('|',
                    RouteGraphGeometry.NormalizeMapName(edge.MapName),
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.MoveMode,
                    edge.Action,
                    edge.ActionParams),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group
                    .OrderBy(edge => GetReviewPreference(edge.ReviewStatus))
                    .ThenByDescending(edge => string.Equals(edge.SourceKind, "telemetry", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(edge => edge.Cost)
                    .ThenBy(edge => edge.EdgeId, StringComparer.OrdinalIgnoreCase)
                    .First();
                representative.SourceCount = group.Sum(edge => Math.Max(1, edge.SourceCount));
                representative.Sources = group
                    .SelectMany(edge => edge.Sources)
                    .GroupBy(source => string.Join('|', source.Repository, source.FileName, source.RouteName, source.Author,
                        source.Kind, source.IsTelemetry, source.IsSyntheticReverse), StringComparer.OrdinalIgnoreCase)
                    .Select(sourceGroup => sourceGroup.First())
                    .ToList();
                if (group.Select(edge => edge.SourceKind).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                {
                    representative.SourceKind = "mixed";
                }

                return representative;
            })
            .ToList();
    }

    private static int GetReviewPreference(GraphReviewStatus reviewStatus)
    {
        return reviewStatus switch
        {
            GraphReviewStatus.Verified => 0,
            GraphReviewStatus.Unreviewed => 1,
            GraphReviewStatus.Risky => 2,
            GraphReviewStatus.Disabled => 3,
            GraphReviewStatus.Rejected => 4,
            _ => 5
        };
    }

    private static string CreateImportedSegmentId(RouteNavigationSourceSegment segment)
    {
        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{segment.MapName}|{segment.Start.X:R},{segment.Start.Y:R}|{segment.End.X:R},{segment.End.Y:R}|{segment.MoveMode}|{segment.Action}|{segment.ActionParams.Length}:{segment.ActionParams}|{segment.AnchorId}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "path_seg_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
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

    private void WriteGraph(RouteNavigationGraph graph)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var tempPath = _graphFilePath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(graph, options));
            File.Move(tempPath, _graphFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}

public sealed class RouteNavigationBuildRequest
{
    public IReadOnlyCollection<RouteHealthEntry> HealthEntries { get; init; } = [];

    public IReadOnlyList<string> PathingTaskDirectories { get; init; } = [];

    public bool IncludeTelemetry { get; init; } = true;

    public double NodeSnapDistance { get; init; } = 6;

    public double MaximumImportedStraightEdgeGameDistance { get; init; } = 300;

    public CancellationToken CancellationToken { get; init; }
}

public sealed class RouteNavigationBuildResult
{
    public bool Success { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public string OutputPath { get; private init; } = string.Empty;

    public RouteNavigationGraph Graph { get; private init; } = new();

    public PathingTaskImportReport? ImportReport { get; private init; }

    internal static RouteNavigationBuildResult Succeeded(
        string outputPath,
        RouteNavigationGraph graph,
        PathingTaskImportReport? importReport)
    {
        return new RouteNavigationBuildResult
        {
            Success = true,
            OutputPath = outputPath,
            Graph = graph,
            ImportReport = importReport
        };
    }

    internal static RouteNavigationBuildResult Failed(
        string outputPath,
        string errorMessage,
        PathingTaskImportReport? importReport = null)
    {
        return new RouteNavigationBuildResult
        {
            OutputPath = outputPath,
            ErrorMessage = errorMessage,
            ImportReport = importReport
        };
    }
}

internal sealed class RouteGraphNodeSnapIndex
{
    private readonly Dictionary<string, RouteNavigationNode> _nodes;
    private readonly Dictionary<string, Dictionary<(int X, int Y), List<RouteNavigationNode>>> _buckets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly double _snapDistance;

    public RouteGraphNodeSnapIndex(
        Dictionary<string, RouteNavigationNode> nodes,
        double snapDistance)
    {
        _nodes = nodes;
        _snapDistance = Math.Max(0, snapDistance);
    }

    public RouteNavigationNode GetOrAdd(
        string mapName,
        RouteGraphPoint point,
        string? excludedNodeId = null)
    {
        var normalizedMapName = RouteGraphGeometry.NormalizeMapName(mapName);
        if (_snapDistance > 0 && TryFindNearby(normalizedMapName, point, excludedNodeId, out var nearby))
        {
            return nearby;
        }

        var nodeId = RouteNavigationNode.CreateNodeId(normalizedMapName, point.X, point.Y);
        if (_nodes.TryGetValue(nodeId, out var existing))
        {
            return existing;
        }

        var node = new RouteNavigationNode
        {
            NodeId = nodeId,
            MapName = normalizedMapName,
            X = Math.Round(point.X, 1),
            Y = Math.Round(point.Y, 1)
        };
        _nodes[nodeId] = node;
        AddToBucket(node);
        return node;
    }

    private bool TryFindNearby(
        string mapName,
        RouteGraphPoint point,
        string? excludedNodeId,
        out RouteNavigationNode node)
    {
        node = null!;
        if (!_buckets.TryGetValue(mapName, out var mapBuckets))
        {
            return false;
        }

        var cell = GetCell(point.X, point.Y);
        var candidates = new List<(RouteNavigationNode Node, double Distance)>();
        for (var x = cell.X - 1; x <= cell.X + 1; x++)
        {
            for (var y = cell.Y - 1; y <= cell.Y + 1; y++)
            {
                if (!mapBuckets.TryGetValue((x, y), out var bucketNodes))
                {
                    continue;
                }

                foreach (var candidate in bucketNodes)
                {
                    if (string.Equals(candidate.NodeId, excludedNodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dx = candidate.X - point.X;
                    var dy = candidate.Y - point.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance <= _snapDistance)
                    {
                        candidates.Add((candidate, distance));
                    }
                }
            }
        }

        var best = candidates
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Node.NodeId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (best.Node == null)
        {
            return false;
        }

        node = best.Node;
        return true;
    }

    private void AddToBucket(RouteNavigationNode node)
    {
        if (_snapDistance <= 0)
        {
            return;
        }

        if (!_buckets.TryGetValue(node.MapName, out var mapBuckets))
        {
            mapBuckets = [];
            _buckets[node.MapName] = mapBuckets;
        }

        var cell = GetCell(node.X, node.Y);
        if (!mapBuckets.TryGetValue(cell, out var bucketNodes))
        {
            bucketNodes = [];
            mapBuckets[cell] = bucketNodes;
        }

        bucketNodes.Add(node);
    }

    private (int X, int Y) GetCell(double x, double y)
    {
        return (
            (int)Math.Floor(x / _snapDistance),
            (int)Math.Floor(y / _snapDistance));
    }
}

public sealed class RouteNavigationGraph
{
    public int SchemaVersion { get; set; } = 3;

    public string GraphId { get; set; } = string.Empty;

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

    /// <summary>通用路网默认为 path；历史任务的 target 不会写入此字段。</summary>
    public string NodeType { get; set; } = "path";

    public string LayerId { get; set; } = "surface";

    public int? Floor { get; set; }

    public bool Underground { get; set; }

    public double? HeightMin { get; set; }

    public double? HeightMax { get; set; }

    public string AreaTag { get; set; } = string.Empty;

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

    public GraphReviewStatus ReviewStatus { get; set; } = GraphReviewStatus.Unreviewed;

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

    public string SourceKind { get; set; } = "telemetry";

    public string SourceRepository { get; set; } = string.Empty;

    public string SourceRouteName { get; set; } = string.Empty;

    public string SourceAuthor { get; set; } = string.Empty;

    public DateTime? LastVerifiedAtUtc { get; set; }

    public List<RouteNavigationEdgeSource> Sources { get; set; } = [];

    public int SourceCount { get; set; } = 1;

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
            ReviewStatus = isSyntheticReverse
                ? GraphReviewStatus.Risky
                : healthStatus switch
                {
                    RouteHealthStatus.Verified => GraphReviewStatus.Verified,
                    RouteHealthStatus.Risky => GraphReviewStatus.Risky,
                    RouteHealthStatus.Disabled => GraphReviewStatus.Disabled,
                    _ => GraphReviewStatus.Unreviewed
                },
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
            SourceKind = "telemetry",
            LastVerifiedAtUtc = healthStatus == RouteHealthStatus.Verified ? health?.LastSuccessUtc : null,
            Sources =
            [
                new RouteNavigationEdgeSource
                {
                    FileName = record.SourceFileName,
                    Kind = "telemetry",
                    IsTelemetry = true,
                    IsSyntheticReverse = isSyntheticReverse
                }
            ],
            SourceCount = 1,
            TargetResourceId = isSyntheticReverse ? string.Empty : record.TargetResourceId,
            TargetResourceLabelId = isSyntheticReverse ? string.Empty : record.TargetResourceLabelId,
            PickedItems = (record.PickedItems ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Points = ResolveEdgePoints(record.Points, isSyntheticReverse)
        };
    }

    public static RouteNavigationEdge FromSourceSegment(
        RouteNavigationSourceSegment segment,
        string segmentId,
        string fromNodeId,
        string toNodeId,
        bool isSyntheticReverse = false)
    {
        var dx = segment.End.X - segment.Start.X;
        var dy = segment.End.Y - segment.Start.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var cost = distance
            * GetHealthPenalty(RouteHealthStatus.Unknown)
            * GetSamplePenalty(null)
            * GetMoveModePenalty(segment.MoveMode)
            * GetActionPenalty(segment.Action);
        var points = new List<TelemetryPoint2D>
        {
            new() { X = (float)segment.Start.X, Y = (float)segment.Start.Y },
            new() { X = (float)segment.End.X, Y = (float)segment.End.Y }
        };
        if (isSyntheticReverse)
        {
            points.Reverse();
        }

        return new RouteNavigationEdge
        {
            EdgeId = isSyntheticReverse ? $"edge_{segmentId}_reverse" : $"edge_{segmentId}",
            SegmentId = segmentId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            MapName = segment.MapName,
            AnchorId = segment.AnchorId,
            SegmentKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{segment.Start.X:F1},{segment.Start.Y:F1}->{segment.End.X:F1},{segment.End.Y:F1}"),
            MoveMode = segment.MoveMode,
            Action = segment.Action,
            ActionParams = segment.ActionParams,
            IsBidirectionalCandidate = segment.IsBidirectionalCandidate,
            IsSyntheticReverse = isSyntheticReverse,
            ReviewStatus = isSyntheticReverse ? GraphReviewStatus.Risky : GraphReviewStatus.Unreviewed,
            HealthStatus = RouteHealthStatus.Unknown,
            Cost = Math.Round(cost, 2),
            AverageDistance = Math.Round(distance, 2),
            SourceRecordId = segment.SourceId,
            SourceFileName = segment.SourceFileName,
            SourceKind = segment.SourceKind,
            SourceRepository = segment.SourceRepository,
            SourceRouteName = segment.SourceRouteName,
            SourceAuthor = segment.SourceAuthor,
            Sources =
            [
                new RouteNavigationEdgeSource
                {
                    FileName = segment.SourceFileName,
                    Repository = segment.SourceRepository,
                    RouteName = segment.SourceRouteName,
                    Author = segment.SourceAuthor,
                    Kind = segment.SourceKind,
                    IsSyntheticReverse = isSyntheticReverse
                }
            ],
            SourceCount = Math.Max(1, segment.SourceCount),
            Points = points
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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GraphReviewStatus
{
    Unreviewed,
    Verified,
    Risky,
    Disabled,
    Rejected
}

public sealed class RouteNavigationEdgeSource
{
    public string Repository { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public bool IsTelemetry { get; set; }

    public bool IsSyntheticReverse { get; set; }
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
