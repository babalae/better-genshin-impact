using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

/// <summary>
/// 路网规划使用的统一成本配置。所有时间均为秒，所有距离均为原神游戏坐标单位。
/// </summary>
public sealed class RouteNavigationCostOptions
{
    public double WalkSpeed { get; init; } = 4.5;

    public double RunSpeed { get; init; } = 6.0;

    public double DashSpeed { get; init; } = 7.5;

    public double SwimSpeed { get; init; } = 3.0;

    public double FlySpeed { get; init; } = 6.0;

    public double ClimbSpeed { get; init; } = 2.0;

    public double JumpSpeed { get; init; } = 4.0;

    public double TeleportDurationSeconds { get; init; } = 18.0;

    public double UnreviewedEdgeCostMultiplier { get; init; } = 1.35;

    public double RiskyEdgeCostMultiplier { get; init; } = 2.0;

    public double SyntheticReverseCostMultiplier { get; init; } = 2.0;

    public double MinimumTeleportSavingsSeconds { get; init; } = 8.0;

    public double LocalDirectMaxGameDistance { get; init; } = 80.0;

    public double ReplanDriftGameDistance { get; init; } = 20.0;

    public double TalkWaitTimeoutSeconds { get; init; } = 600.0;

    public int LocalIconMissRetryCount { get; init; } = 5;

    public double LocalFollowTimeoutSeconds { get; init; } = 180.0;

    public int LocalRecognitionRetryDelayMilliseconds { get; init; } = 1000;

    public int LocalForwardStepMilliseconds { get; init; } = 200;

    public int LocalJumpIntervalMilliseconds { get; init; } = 200;

    public int LocalSettleMilliseconds { get; init; } = 200;

    public double LocalArrivalGameDistance { get; init; } = 3.0;

    public double LocalTemplateThreshold { get; init; } = 0.8;

    public double LocalIconCenterX { get; init; } = 960.0;

    public double LocalIconCenterTolerance { get; init; } = 40.0;

    public double LocalIconMaximumY { get; init; } = 540.0;

    public int LocalMouseAdjustmentUnit { get; init; } = 20;

    public int LocalVerticalMouseAdjustment { get; init; } = 920;
}

public enum RouteNavigationCostSource
{
    Telemetry,
    Estimated
}

public sealed record RouteNavigationCostBreakdown(
    string Component,
    double Seconds,
    RouteNavigationCostSource Source,
    double GameDistance = 0)
{
    public string SourceLabel => Source == RouteNavigationCostSource.Telemetry ? "遥测值" : "估算值";

    public bool IsValid => double.IsFinite(Seconds) && Seconds >= 0;
}

public interface IRouteNavigationCostModel
{
    RouteNavigationCostBreakdown EvaluateEdge(
        string mapName,
        string? mapMatchMethod,
        RouteNavigationEdge edge,
        RouteNavigationCostOptions options);

    RouteNavigationCostBreakdown EvaluateConnector(
        string mapName,
        string? mapMatchMethod,
        RouteGraphPoint from,
        RouteGraphPoint to,
        string moveMode,
        RouteNavigationCostOptions options,
        string component);

    RouteNavigationCostBreakdown EvaluateTeleport(RouteNavigationCostOptions options);
}

/// <summary>
/// 把遥测时间、普通路线距离和传送统一换算为预计耗时秒数。
/// </summary>
public sealed class RouteNavigationCostModel(IRouteCoordinateConverter coordinateConverter) : IRouteNavigationCostModel
{
    public RouteNavigationCostBreakdown EvaluateEdge(
        string mapName,
        string? mapMatchMethod,
        RouteNavigationEdge edge,
        RouteNavigationCostOptions options)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(options);

        var distance = TryCalculateGameDistance(mapName, mapMatchMethod, edge.Points, out var measuredDistance)
            ? measuredDistance
            : 0;

        if (IsTelemetry(edge) && edge.AverageDurationMs > 0)
        {
            return new RouteNavigationCostBreakdown(
                "edge",
                edge.AverageDurationMs / 1000.0,
                RouteNavigationCostSource.Telemetry,
                distance);
        }

        if (distance <= 0)
        {
            return new RouteNavigationCostBreakdown(
                "edge",
                double.PositiveInfinity,
                RouteNavigationCostSource.Estimated);
        }

        return new RouteNavigationCostBreakdown(
            "edge",
            distance / ResolveSpeed(edge.MoveMode, options),
            RouteNavigationCostSource.Estimated,
            distance);
    }

    public RouteNavigationCostBreakdown EvaluateConnector(
        string mapName,
        string? mapMatchMethod,
        RouteGraphPoint from,
        RouteGraphPoint to,
        string moveMode,
        RouteNavigationCostOptions options,
        string component)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (RouteGraphGeometry.Distance(from, to) <= 0.0001)
        {
            return new RouteNavigationCostBreakdown(
                component,
                0,
                RouteNavigationCostSource.Estimated);
        }

        if (!coordinateConverter.TryImageToGame(mapName, mapMatchMethod, from, out var fromGame) ||
            !coordinateConverter.TryImageToGame(mapName, mapMatchMethod, to, out var toGame))
        {
            return new RouteNavigationCostBreakdown(
                component,
                double.PositiveInfinity,
                RouteNavigationCostSource.Estimated);
        }

        var distance = Distance(fromGame, toGame);
        return new RouteNavigationCostBreakdown(
            component,
            distance / ResolveSpeed(moveMode, options),
            RouteNavigationCostSource.Estimated,
            distance);
    }

    public RouteNavigationCostBreakdown EvaluateTeleport(RouteNavigationCostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new RouteNavigationCostBreakdown(
            "teleport",
            Math.Max(0, options.TeleportDurationSeconds),
            RouteNavigationCostSource.Estimated);
    }

    private bool TryCalculateGameDistance(
        string mapName,
        string? mapMatchMethod,
        IReadOnlyList<TelemetryPoint2D>? points,
        out double distance)
    {
        distance = 0;
        if (points is not { Count: >= 2 })
        {
            return false;
        }

        if (!coordinateConverter.TryImageToGame(
                mapName,
                mapMatchMethod,
                new RouteGraphPoint(points[0].X, points[0].Y),
                out var previous))
        {
            return false;
        }

        for (var index = 1; index < points.Count; index++)
        {
            if (!coordinateConverter.TryImageToGame(
                    mapName,
                    mapMatchMethod,
                    new RouteGraphPoint(points[index].X, points[index].Y),
                    out var current))
            {
                distance = 0;
                return false;
            }

            distance += Distance(previous, current);
            previous = current;
        }

        return distance > 0;
    }

    private static bool IsTelemetry(RouteNavigationEdge edge)
    {
        return edge.SourceKind.Contains("telemetry", StringComparison.OrdinalIgnoreCase);
    }

    private static double ResolveSpeed(string? moveMode, RouteNavigationCostOptions options)
    {
        var speed = moveMode?.ToLowerInvariant() switch
        {
            "run" => options.RunSpeed,
            "dash" => options.DashSpeed,
            "swim" => options.SwimSpeed,
            "fly" => options.FlySpeed,
            "climb" => options.ClimbSpeed,
            "jump" => options.JumpSpeed,
            _ => options.WalkSpeed
        };
        return Math.Max(0.1, speed);
    }

    private static double Distance(RouteGamePoint from, RouteGamePoint to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
