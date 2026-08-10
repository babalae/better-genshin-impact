using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

/// <summary>
/// Generates a stable identity for the generated topology. Timestamps and review overrides are deliberately excluded.
/// </summary>
public static class RouteNavigationGraphIdentity
{
    public static string Compute(RouteNavigationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var canonical = new StringBuilder();
        foreach (var node in graph.Nodes.OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append("N|").Append(node.NodeId).Append('|').Append(node.MapName).Append('|')
                .Append(node.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(node.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(node.LayerId).Append('|').Append(node.Floor).Append('|').Append(node.Underground).Append('|')
                .Append(string.Join(',', node.AnchorIds.Order(StringComparer.OrdinalIgnoreCase))).AppendLine();
        }

        foreach (var edge in graph.Edges.OrderBy(item => item.EdgeId, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append("E|").Append(edge.EdgeId).Append('|').Append(edge.FromNodeId).Append('|')
                .Append(edge.ToNodeId).Append('|').Append(edge.MoveMode).Append('|').Append(edge.Action).Append('|')
                .Append(edge.ActionParams).Append('|').Append(edge.IsSyntheticReverse);
            foreach (var point in edge.Points)
            {
                canonical.Append('|').Append(point.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
            }
            canonical.AppendLine();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return "graph_" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    public static string ComputeEffective(
        RouteNavigationGraph graph,
        IReadOnlyList<RouteGraphTeleportEntry> teleports)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(teleports);
        var canonical = new StringBuilder();
        foreach (var node in (graph.Nodes ?? []).OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append("N|").Append(node.NodeId).Append('|').Append(node.MapName).Append('|')
                .Append(node.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(node.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(node.NodeType).Append('|').Append(node.LayerId).Append('|').Append(node.Floor).Append('|')
                .Append(node.Underground).Append('|')
                .Append(node.HeightMin?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(node.HeightMax?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(node.AreaTag).Append('|')
                .Append(string.Join(',', (node.AnchorIds ?? []).Order(StringComparer.OrdinalIgnoreCase))).Append('|')
                .Append(string.Join(',', (node.ResourceIds ?? []).Order(StringComparer.OrdinalIgnoreCase))).Append('|')
                .Append(string.Join(',', (node.ResourceLabelIds ?? []).Order(StringComparer.OrdinalIgnoreCase))).AppendLine();
        }

        foreach (var edge in (graph.Edges ?? []).OrderBy(item => item.EdgeId, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append("E|").Append(edge.EdgeId).Append('|').Append(edge.SegmentId).Append('|')
                .Append(edge.FromNodeId).Append('|').Append(edge.ToNodeId).Append('|').Append(edge.MapName).Append('|')
                .Append(edge.AnchorId).Append('|').Append(edge.SegmentKey).Append('|').Append(edge.MoveMode).Append('|')
                .Append(edge.Action).Append('|').Append(edge.ActionParams).Append('|').Append(edge.IsBidirectionalCandidate).Append('|')
                .Append(edge.IsSyntheticReverse).Append('|').Append(edge.ReviewStatus).Append('|').Append(edge.HealthStatus).Append('|')
                .Append(edge.Cost.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(edge.AverageDistance.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(edge.AverageDurationMs.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(edge.SourceKind).Append('|').Append(edge.TargetResourceId).Append('|')
                .Append(edge.TargetResourceLabelId);
            foreach (var source in (edge.Sources ?? [])
                         .OrderBy(item => item.Repository, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.RouteName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Kind, StringComparer.OrdinalIgnoreCase))
            {
                canonical.Append("|S,").Append(source.Repository).Append(',').Append(source.FileName).Append(',')
                    .Append(source.RouteName).Append(',').Append(source.Author).Append(',').Append(source.Kind).Append(',')
                    .Append(source.IsTelemetry).Append(',').Append(source.IsSyntheticReverse);
            }
            foreach (var point in edge.Points ?? [])
            {
                canonical.Append('|').Append(point.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
            }
            canonical.AppendLine();
        }

        foreach (var teleport in teleports.OrderBy(item => item.AnchorId, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append("T|").Append(teleport.MapName).Append('|').Append(teleport.AnchorId).Append('|')
                .Append(teleport.Id).Append('|').Append(teleport.Type).Append('|')
                .Append(teleport.GameX.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(teleport.GameY.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(teleport.ImageX.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(teleport.ImageY.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(teleport.SpawnGameX.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(teleport.SpawnGameY.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(teleport.SpawnImageX.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(teleport.SpawnImageY.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return "effective_" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
