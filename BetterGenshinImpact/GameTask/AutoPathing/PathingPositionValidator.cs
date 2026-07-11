using System;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public sealed record PathingPositionValidationResult(
    bool IsValid,
    string Reason,
    double JumpDistance,
    double SegmentDeviation,
    double TargetDistance);

public static class PathingPositionValidator
{
    private const double JumpDistanceThreshold = 150.0;
    private const double SegmentDeviationThreshold = 100.0;
    private const double NavigationBreakDistanceThreshold = 1000.0;

    public static readonly Point2f UnknownPosition = new(float.NaN, float.NaN);

    public static PathingPositionValidationResult Validate(
        Point2f position,
        WaypointForTrack target,
        WaypointForTrack? previous,
        Point2f? lastValidPosition,
        bool ignoreContinuity = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        var targetDistance = Navigation.GetDistance(target, position);

        if (!IsKnownPosition(position))
        {
            return new PathingPositionValidationResult(false, "position_unrecognized", 0, 0, targetDistance);
        }

        var jumpDistance = 0.0;
        if (!ignoreContinuity && lastValidPosition is { } validPosition && IsKnownPosition(validPosition))
        {
            jumpDistance = GetDistance(position, validPosition);
            if (jumpDistance > JumpDistanceThreshold)
            {
                return new PathingPositionValidationResult(false, "position_jump", jumpDistance, 0, targetDistance);
            }
        }

        var segmentDeviation = 0.0;
        if (!ignoreContinuity && previous != null)
        {
            segmentDeviation = GetPointToSegmentDistance(position, previous, target);
            if (segmentDeviation > SegmentDeviationThreshold)
            {
                return new PathingPositionValidationResult(false, "segment_deviation", jumpDistance, segmentDeviation, targetDistance);
            }
        }

        return new PathingPositionValidationResult(true, string.Empty, jumpDistance, segmentDeviation, targetDistance);
    }

    public static bool IsKnownPosition(Point2f position)
    {
        return !float.IsNaN(position.X) && !float.IsNaN(position.Y);
    }

    public static bool IsNavigationBreak(WaypointForTrack? previous, WaypointForTrack target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (previous == null || previous.Type == WaypointType.Teleport.Code)
        {
            return false;
        }

        return Navigation.GetDistance(previous, new Point2f((float)target.X, (float)target.Y)) > NavigationBreakDistanceThreshold;
    }

    private static double GetPointToSegmentDistance(Point2f point, Waypoint start, Waypoint end)
    {
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var segmentLengthSquared = (segmentX * segmentX) + (segmentY * segmentY);

        if (segmentLengthSquared <= double.Epsilon)
        {
            return Navigation.GetDistance(start, point);
        }

        var pointX = point.X - start.X;
        var pointY = point.Y - start.Y;
        var projection = ((pointX * segmentX) + (pointY * segmentY)) / segmentLengthSquared;
        projection = Math.Clamp(projection, 0.0, 1.0);

        var closestX = start.X + (projection * segmentX);
        var closestY = start.Y + (projection * segmentY);
        var distanceX = point.X - closestX;
        var distanceY = point.Y - closestY;

        return Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
    }

    private static double GetDistance(Point2f a, Point2f b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
