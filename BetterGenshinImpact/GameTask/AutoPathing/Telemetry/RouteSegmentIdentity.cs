using BetterGenshinImpact.GameTask.AutoPathing.Model;
using OpenCvSharp;
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteSegmentIdentity
{
    public string SegmentId { get; private set; } = string.Empty;

    public string MapName { get; init; } = string.Empty;

    public string AnchorId { get; init; } = string.Empty;

    public string StartKey { get; init; } = string.Empty;

    public string EndKey { get; init; } = string.Empty;

    public string MoveMode { get; init; } = string.Empty;

    public string WaypointType { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string ActionParams { get; init; } = string.Empty;

    public string SegmentKey => $"{StartKey}->{EndKey}";

    public static RouteSegmentIdentity Create(
        WaypointForTrack? previous,
        WaypointForTrack target,
        Point2f startPoint,
        string anchorId)
    {
        var plannedStartPoint = previous == null
            ? startPoint
            : new Point2f((float)previous.X, (float)previous.Y);
        var plannedEndPoint = new Point2f((float)target.X, (float)target.Y);

        var identity = new RouteSegmentIdentity
        {
            MapName = target.MapName ?? "Teyvat",
            AnchorId = string.IsNullOrWhiteSpace(anchorId) ? "Unknown" : anchorId,
            StartKey = FormatPoint(plannedStartPoint),
            EndKey = FormatPoint(plannedEndPoint),
            MoveMode = target.MoveMode ?? string.Empty,
            WaypointType = target.Type ?? string.Empty,
            Action = target.Action ?? string.Empty,
            ActionParams = target.ActionParams ?? string.Empty
        };

        identity.SegmentId = CreateStableId(identity, previous);
        return identity;
    }

    private static string FormatPoint(Point2f point)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(point.X, 1):F1},{Math.Round(point.Y, 1):F1}");
    }

    private static string CreateStableId(RouteSegmentIdentity identity, WaypointForTrack? previous)
    {
        var previousKey = previous == null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(previous.X, 1):F1},{Math.Round(previous.Y, 1):F1}");

        var raw = string.Join('|',
            identity.MapName,
            identity.AnchorId,
            previousKey,
            identity.StartKey,
            identity.EndKey,
            identity.MoveMode,
            identity.WaypointType,
            identity.Action,
            identity.ActionParams);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "seg_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
