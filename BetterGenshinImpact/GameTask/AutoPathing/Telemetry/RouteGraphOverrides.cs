using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteGraphOverridePatch
{
    public string Id { get; set; } = string.Empty;

    public string BaseGraphId { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<RouteGraphOverrideOperation> Operations { get; set; } = [];

    [JsonIgnore]
    public string SourceFileName { get; set; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RouteGraphOverrideOperationType
{
    DisableEdge,
    RestoreEdge,
    DeleteEdge,
    AddEdge,
    SetEdgeReview,
    AddNode,
    MoveNode,
    DeleteNode,
    SetNodeLayer,
    SetNodeType,
    AssociateTeleport,
    RemoveTeleportAssociation
}

public sealed class RouteGraphOverrideOperation
{
    public RouteGraphOverrideOperationType Type { get; set; }

    public string EdgeId { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public RouteNavigationEdge? Edge { get; set; }

    public RouteNavigationNode? Node { get; set; }

    public GraphReviewStatus ReviewStatus { get; set; } = GraphReviewStatus.Unreviewed;

    public double? X { get; set; }

    public double? Y { get; set; }

    public string LayerId { get; set; } = string.Empty;

    public int? Floor { get; set; }

    public bool? Underground { get; set; }

    public string AreaTag { get; set; } = string.Empty;

    public string NodeType { get; set; } = string.Empty;

    public string TeleportAnchorId { get; set; } = string.Empty;
}

public sealed class RouteGraphOverrideApplyResult
{
    public bool Succeeded => Errors.Count == 0;

    public List<string> AppliedPatchIds { get; } = [];

    public List<string> IsolatedPatchIds { get; } = [];

    public List<string> Errors { get; } = [];
}

public static class RouteGraphMutationService
{
    public static bool MoveNode(RouteNavigationGraph graph, string nodeId, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var node = graph.Nodes.FirstOrDefault(item =>
            string.Equals(item.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        if (node == null)
        {
            return false;
        }

        node.X = x;
        node.Y = y;
        foreach (var edge in graph.Edges.Where(item =>
                     string.Equals(item.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.ToNodeId, nodeId, StringComparison.OrdinalIgnoreCase)))
        {
            var fromNode = graph.Nodes.FirstOrDefault(item =>
                string.Equals(item.NodeId, edge.FromNodeId, StringComparison.OrdinalIgnoreCase));
            var toNode = graph.Nodes.FirstOrDefault(item =>
                string.Equals(item.NodeId, edge.ToNodeId, StringComparison.OrdinalIgnoreCase));
            if (fromNode == null || toNode == null)
            {
                continue;
            }

            if (edge.Points is not { Count: >= 2 })
            {
                edge.Points =
                [
                    new TelemetryPoint2D { X = (float)fromNode.X, Y = (float)fromNode.Y },
                    new TelemetryPoint2D { X = (float)toNode.X, Y = (float)toNode.Y }
                ];
                continue;
            }

            if (string.Equals(edge.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                edge.Points[0].X = (float)x;
                edge.Points[0].Y = (float)y;
            }

            if (string.Equals(edge.ToNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                edge.Points[^1].X = (float)x;
                edge.Points[^1].Y = (float)y;
            }
        }

        return true;
    }
}

public sealed class RouteGraphOverrideApplier
{
    public RouteGraphOverrideApplyResult Apply(
        RouteNavigationGraph graph,
        IEnumerable<RouteGraphOverridePatch> patches)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(patches);
        var result = new RouteGraphOverrideApplyResult();

        foreach (var patch in patches
                     .OrderBy(item => item.SourceFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var patchId = string.IsNullOrWhiteSpace(patch.Id) ? patch.SourceFileName : patch.Id;
            if (!string.IsNullOrWhiteSpace(patch.BaseGraphId) &&
                !string.Equals(patch.BaseGraphId, graph.GraphId, StringComparison.OrdinalIgnoreCase))
            {
                result.IsolatedPatchIds.Add(patchId);
                continue;
            }

            if (!TryValidate(graph, patch, out var validationError))
            {
                result.IsolatedPatchIds.Add(patchId);
                result.Errors.Add($"{patchId}: {validationError}");
                continue;
            }

            foreach (var operation in patch.Operations)
            {
                ApplyOperation(graph, operation);
            }

            result.AppliedPatchIds.Add(patchId);
        }

        return result;
    }

    private static bool TryValidate(
        RouteNavigationGraph graph,
        RouteGraphOverridePatch patch,
        out string error)
    {
        var nodeIds = graph.Nodes.Select(node => node.NodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edgeEndpoints = graph.Edges.ToDictionary(
            edge => edge.EdgeId,
            edge => (edge.FromNodeId, edge.ToNodeId),
            StringComparer.OrdinalIgnoreCase);
        foreach (var operation in patch.Operations)
        {
            switch (operation.Type)
            {
                case RouteGraphOverrideOperationType.DisableEdge:
                case RouteGraphOverrideOperationType.RestoreEdge:
                case RouteGraphOverrideOperationType.DeleteEdge:
                case RouteGraphOverrideOperationType.SetEdgeReview:
                    if (!edgeEndpoints.ContainsKey(operation.EdgeId))
                    {
                        error = $"edge not found: {operation.EdgeId}";
                        return false;
                    }
                    if (operation.Type == RouteGraphOverrideOperationType.DeleteEdge)
                    {
                        edgeEndpoints.Remove(operation.EdgeId);
                    }
                    break;
                case RouteGraphOverrideOperationType.AddEdge:
                    if (operation.Edge == null || string.IsNullOrWhiteSpace(operation.Edge.EdgeId))
                    {
                        error = "addEdge requires edge";
                        return false;
                    }
                    if (edgeEndpoints.ContainsKey(operation.Edge.EdgeId) ||
                        !nodeIds.Contains(operation.Edge.FromNodeId) ||
                        !nodeIds.Contains(operation.Edge.ToNodeId))
                    {
                        error = $"invalid added edge: {operation.Edge.EdgeId}";
                        return false;
                    }
                    edgeEndpoints.Add(
                        operation.Edge.EdgeId,
                        (operation.Edge.FromNodeId, operation.Edge.ToNodeId));
                    break;
                case RouteGraphOverrideOperationType.AddNode:
                    if (operation.Node == null || string.IsNullOrWhiteSpace(operation.Node.NodeId) ||
                        !nodeIds.Add(operation.Node.NodeId))
                    {
                        error = "invalid added node";
                        return false;
                    }
                    break;
                case RouteGraphOverrideOperationType.MoveNode:
                case RouteGraphOverrideOperationType.SetNodeLayer:
                case RouteGraphOverrideOperationType.SetNodeType:
                case RouteGraphOverrideOperationType.AssociateTeleport:
                case RouteGraphOverrideOperationType.RemoveTeleportAssociation:
                    if (!nodeIds.Contains(operation.NodeId))
                    {
                        error = $"node not found: {operation.NodeId}";
                        return false;
                    }
                    break;
                case RouteGraphOverrideOperationType.DeleteNode:
                    if (!nodeIds.Remove(operation.NodeId))
                    {
                        error = $"node not found: {operation.NodeId}";
                        return false;
                    }
                    foreach (var edgeId in edgeEndpoints
                                 .Where(item =>
                                     string.Equals(item.Value.FromNodeId, operation.NodeId, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(item.Value.ToNodeId, operation.NodeId, StringComparison.OrdinalIgnoreCase))
                                 .Select(item => item.Key)
                                 .ToList())
                    {
                        edgeEndpoints.Remove(edgeId);
                    }
                    break;
                default:
                    error = $"unsupported operation: {operation.Type}";
                    return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static void ApplyOperation(RouteNavigationGraph graph, RouteGraphOverrideOperation operation)
    {
        var edge = graph.Edges.FirstOrDefault(item =>
            string.Equals(item.EdgeId, operation.EdgeId, StringComparison.OrdinalIgnoreCase));
        var node = graph.Nodes.FirstOrDefault(item =>
            string.Equals(item.NodeId, operation.NodeId, StringComparison.OrdinalIgnoreCase));

        switch (operation.Type)
        {
            case RouteGraphOverrideOperationType.DisableEdge:
                edge!.ReviewStatus = GraphReviewStatus.Disabled;
                edge.HealthStatus = RouteHealthStatus.Disabled;
                break;
            case RouteGraphOverrideOperationType.RestoreEdge:
                edge!.ReviewStatus = GraphReviewStatus.Unreviewed;
                edge.HealthStatus = RouteHealthStatus.Unknown;
                break;
            case RouteGraphOverrideOperationType.DeleteEdge:
                graph.Edges.Remove(edge!);
                break;
            case RouteGraphOverrideOperationType.AddEdge:
                operation.Edge!.SourceKind = string.IsNullOrWhiteSpace(operation.Edge.SourceKind)
                    ? "manual-override"
                    : operation.Edge.SourceKind;
                graph.Edges.Add(operation.Edge);
                break;
            case RouteGraphOverrideOperationType.SetEdgeReview:
                edge!.ReviewStatus = operation.ReviewStatus;
                if (operation.ReviewStatus is GraphReviewStatus.Disabled or GraphReviewStatus.Rejected)
                {
                    edge.HealthStatus = RouteHealthStatus.Disabled;
                }
                else if (operation.ReviewStatus == GraphReviewStatus.Verified)
                {
                    edge.HealthStatus = RouteHealthStatus.Verified;
                    edge.LastVerifiedAtUtc = DateTime.UtcNow;
                }
                else if (operation.ReviewStatus == GraphReviewStatus.Risky)
                {
                    edge.HealthStatus = RouteHealthStatus.Risky;
                }
                break;
            case RouteGraphOverrideOperationType.AddNode:
                graph.Nodes.Add(operation.Node!);
                break;
            case RouteGraphOverrideOperationType.MoveNode:
                RouteGraphMutationService.MoveNode(
                    graph,
                    operation.NodeId,
                    operation.X ?? node!.X,
                    operation.Y ?? node!.Y);
                break;
            case RouteGraphOverrideOperationType.DeleteNode:
                graph.Edges.RemoveAll(item =>
                    string.Equals(item.FromNodeId, operation.NodeId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ToNodeId, operation.NodeId, StringComparison.OrdinalIgnoreCase));
                graph.Nodes.Remove(node!);
                break;
            case RouteGraphOverrideOperationType.SetNodeLayer:
                node!.LayerId = string.IsNullOrWhiteSpace(operation.LayerId) ? node.LayerId : operation.LayerId;
                node.Floor = operation.Floor;
                node.Underground = operation.Underground ?? node.Underground;
                node.AreaTag = string.IsNullOrWhiteSpace(operation.AreaTag) ? node.AreaTag : operation.AreaTag;
                break;
            case RouteGraphOverrideOperationType.SetNodeType:
                node!.NodeType = string.IsNullOrWhiteSpace(operation.NodeType) ? "path" : operation.NodeType;
                break;
            case RouteGraphOverrideOperationType.AssociateTeleport:
                if (!string.IsNullOrWhiteSpace(operation.TeleportAnchorId))
                {
                    node!.AnchorIds.Add(operation.TeleportAnchorId);
                }
                break;
            case RouteGraphOverrideOperationType.RemoveTeleportAssociation:
                node!.AnchorIds.Remove(operation.TeleportAnchorId);
                break;
        }
    }
}

public sealed class RouteGraphOverrideLoadResult
{
    public List<RouteGraphOverridePatch> Patches { get; } = [];

    public List<string> Errors { get; } = [];
}

public sealed class RouteGraphOverrideStore
{
    public const string DirectoryName = "GraphOverrides";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public RouteGraphOverrideStore(string graphDirectory)
    {
        DirectoryPath = Path.Combine(graphDirectory, DirectoryName);
    }

    public string DirectoryPath { get; }

    public RouteGraphOverrideLoadResult LoadAll()
    {
        var result = new RouteGraphOverrideLoadResult();
        if (!Directory.Exists(DirectoryPath))
        {
            return result;
        }

        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var patch = JsonSerializer.Deserialize<RouteGraphOverridePatch>(File.ReadAllText(path), JsonOptions);
                if (patch == null || patch.Operations.Count == 0)
                {
                    result.Errors.Add($"{Path.GetFileName(path)}: empty patch");
                    continue;
                }

                patch.SourceFileName = Path.GetFileName(path);
                result.Patches.Add(patch);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                result.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return result;
    }

    public string Save(RouteGraphOverridePatch patch, string? preferredFileName = null)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.Operations.Count == 0)
        {
            throw new InvalidOperationException("override patch has no operations");
        }

        ValidateForSave(patch);

        Directory.CreateDirectory(DirectoryPath);
        var baseName = string.IsNullOrWhiteSpace(preferredFileName) ? patch.Id : preferredFileName;
        baseName = SanitizeFileName(string.IsNullOrWhiteSpace(baseName)
            ? $"graph-fix-{DateTime.UtcNow:yyyyMMdd-HHmmss}"
            : baseName);
        var filePath = Path.Combine(DirectoryPath, Path.ChangeExtension(baseName, ".json"));
        if (string.IsNullOrWhiteSpace(preferredFileName) && File.Exists(filePath))
        {
            filePath = Path.Combine(
                DirectoryPath,
                Path.ChangeExtension($"{baseName}-{Guid.NewGuid().ToString("N")[..8]}", ".json"));
        }

        var tempPath = filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(patch, JsonOptions);
            var roundTrip = JsonSerializer.Deserialize<RouteGraphOverridePatch>(json, JsonOptions);
            if (roundTrip == null || roundTrip.Operations.Count != patch.Operations.Count)
            {
                throw new InvalidOperationException("override patch failed JSON round-trip validation");
            }

            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, filePath, true);
            return filePath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void ValidateForSave(RouteGraphOverridePatch patch)
    {
        if (string.IsNullOrWhiteSpace(patch.Id))
        {
            throw new InvalidOperationException("override patch id is required");
        }

        for (var index = 0; index < patch.Operations.Count; index++)
        {
            var operation = patch.Operations[index];
            var prefix = $"operation {index + 1} ({operation.Type})";
            switch (operation.Type)
            {
                case RouteGraphOverrideOperationType.AddNode:
                    if (operation.Node == null || string.IsNullOrWhiteSpace(operation.Node.NodeId))
                    {
                        throw new InvalidOperationException($"{prefix}: addNode requires a node with id");
                    }
                    EnsureFinite(operation.Node.X, operation.Node.Y, prefix);
                    break;
                case RouteGraphOverrideOperationType.AddEdge:
                    if (operation.Edge == null ||
                        string.IsNullOrWhiteSpace(operation.Edge.EdgeId) ||
                        string.IsNullOrWhiteSpace(operation.Edge.FromNodeId) ||
                        string.IsNullOrWhiteSpace(operation.Edge.ToNodeId))
                    {
                        throw new InvalidOperationException($"{prefix}: addEdge requires an edge id and both endpoints");
                    }
                    foreach (var point in operation.Edge.Points)
                    {
                        EnsureFinite(point.X, point.Y, prefix);
                    }
                    break;
                case RouteGraphOverrideOperationType.MoveNode:
                    if (string.IsNullOrWhiteSpace(operation.NodeId))
                    {
                        throw new InvalidOperationException($"{prefix}: node id is required");
                    }
                    if ((operation.X.HasValue && !double.IsFinite(operation.X.Value)) ||
                        (operation.Y.HasValue && !double.IsFinite(operation.Y.Value)))
                    {
                        throw new InvalidOperationException($"{prefix}: node coordinates must be finite");
                    }
                    break;
                case RouteGraphOverrideOperationType.DisableEdge:
                case RouteGraphOverrideOperationType.RestoreEdge:
                case RouteGraphOverrideOperationType.DeleteEdge:
                case RouteGraphOverrideOperationType.SetEdgeReview:
                    if (string.IsNullOrWhiteSpace(operation.EdgeId))
                    {
                        throw new InvalidOperationException($"{prefix}: edge id is required");
                    }
                    break;
                case RouteGraphOverrideOperationType.DeleteNode:
                case RouteGraphOverrideOperationType.SetNodeLayer:
                case RouteGraphOverrideOperationType.SetNodeType:
                case RouteGraphOverrideOperationType.AssociateTeleport:
                case RouteGraphOverrideOperationType.RemoveTeleportAssociation:
                    if (string.IsNullOrWhiteSpace(operation.NodeId))
                    {
                        throw new InvalidOperationException($"{prefix}: node id is required");
                    }
                    break;
            }
        }
    }

    private static void EnsureFinite(double x, double y, string prefix)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new InvalidOperationException($"{prefix}: coordinates must be finite");
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return value.Trim();
    }
}
