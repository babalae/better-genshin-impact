using System;
using System.Globalization;
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
}
