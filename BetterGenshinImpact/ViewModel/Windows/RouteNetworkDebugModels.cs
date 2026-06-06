using System.Collections.Generic;

namespace BetterGenshinImpact.ViewModel.Windows;

internal sealed record RouteLiteDiagnosticsResult(
    string GraphSummary,
    string HealthSummary,
    IReadOnlyList<RouteHealthRow> HealthRows);

internal sealed record RouteGraphDiagnosticsResult(
    string Summary,
    IReadOnlyList<RouteNearbyNodeRow> NearbyNodes,
    IReadOnlyList<RouteNearbyEdgeRow> NearbyEdges)
{
    public static RouteGraphDiagnosticsResult Empty(string summary)
    {
        return new RouteGraphDiagnosticsResult(summary, [], []);
    }
}

public sealed class RoutePlanEdgeRow
{
    public int Index { get; init; }

    public string FromNodeId { get; init; } = string.Empty;

    public string ToNodeId { get; init; } = string.Empty;

    public double Cost { get; init; }

    public string MoveMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string HealthStatus { get; init; } = string.Empty;

    public bool IsSyntheticReverse { get; init; }

    public bool IsBidirectionalCandidate { get; init; }
}

public sealed class RouteNearbyNodeRow
{
    public string NodeId { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Distance { get; init; }

    public int Anchors { get; init; }

    public int Resources { get; init; }
}

public sealed class RouteNearbyEdgeRow
{
    public string EdgeId { get; init; } = string.Empty;

    public double Distance { get; init; }

    public double Cost { get; init; }

    public string MoveMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string HealthStatus { get; init; } = string.Empty;

    public bool Reverse { get; init; }
}

public sealed class RouteHealthRow
{
    public string SegmentId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int Success { get; init; }

    public int Failure { get; init; }

    public double Rate { get; init; }

    public string MoveMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string LastFailure { get; init; } = string.Empty;
}
