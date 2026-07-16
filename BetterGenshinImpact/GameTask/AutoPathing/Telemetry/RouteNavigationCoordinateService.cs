using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

/// <summary>
/// PathExecutor 使用的原神世界坐标。该类型用于阻止图像坐标与游戏坐标被静默混用。
/// </summary>
public readonly record struct RouteGamePoint(double X, double Y);

public sealed record RouteCurrentPosition(string MapName, RouteGraphPoint ImagePoint);

public interface IRouteCurrentPositionResolver
{
    bool TryResolve(
        ImageRegion screen,
        string preferredMapName,
        string? mapMatchMethod,
        out RouteCurrentPosition position);
}

/// <summary>
/// 目标导航唯一允许使用的地图坐标转换入口。
/// </summary>
public interface IRouteCoordinateConverter
{
    bool TryImageToGame(
        string mapName,
        string? mapMatchMethod,
        RouteGraphPoint imagePoint,
        out RouteGamePoint gamePoint);

    bool TryGameToImage(
        string mapName,
        string? mapMatchMethod,
        RouteGamePoint gamePoint,
        out RouteGraphPoint imagePoint);
}

public sealed class RouteNavigationCoordinateService : IRouteCoordinateConverter
{
    public static RouteNavigationCoordinateService Instance { get; } = new();

    private RouteNavigationCoordinateService()
    {
    }

    public bool TryImageToGame(
        string mapName,
        string? mapMatchMethod,
        RouteGraphPoint imagePoint,
        out RouteGamePoint gamePoint)
    {
        gamePoint = default;
        if (!IsFinite(imagePoint.X) || !IsFinite(imagePoint.Y))
        {
            return false;
        }

        try
        {
            var map = MapManager.GetMap(RouteGraphGeometry.NormalizeMapName(mapName), mapMatchMethod ?? string.Empty);
            var converted = map?.ConvertImageCoordinatesToGenshinMapCoordinates(
                new Point2f((float)imagePoint.X, (float)imagePoint.Y));
            if (converted == null || !IsFinite(converted.Value.X) || !IsFinite(converted.Value.Y))
            {
                return false;
            }

            gamePoint = new RouteGamePoint(converted.Value.X, converted.Value.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGameToImage(
        string mapName,
        string? mapMatchMethod,
        RouteGamePoint gamePoint,
        out RouteGraphPoint imagePoint)
    {
        imagePoint = default;
        if (!IsFinite(gamePoint.X) || !IsFinite(gamePoint.Y))
        {
            return false;
        }

        try
        {
            var map = MapManager.GetMap(RouteGraphGeometry.NormalizeMapName(mapName), mapMatchMethod ?? string.Empty);
            if (map == null)
            {
                return false;
            }

            var converted = map.ConvertGenshinMapCoordinatesToImageCoordinates(
                new Point2f((float)gamePoint.X, (float)gamePoint.Y));
            if (!IsFinite(converted.X) || !IsFinite(converted.Y))
            {
                return false;
            }

            imagePoint = new RouteGraphPoint(converted.X, converted.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

/// <summary>
/// 从实时小地图识别图像坐标，并明确返回该坐标所属的地图层级。
/// </summary>
public sealed class RouteCurrentPositionResolver : IRouteCurrentPositionResolver
{
    public static RouteCurrentPositionResolver Instance { get; } = new();

    private readonly object _syncRoot = new();

    private RouteCurrentPositionResolver()
    {
    }

    public bool TryResolve(
        ImageRegion screen,
        string preferredMapName,
        string? mapMatchMethod,
        out RouteCurrentPosition position)
    {
        ArgumentNullException.ThrowIfNull(screen);
        position = null!;
        var matchingMethod = string.IsNullOrWhiteSpace(mapMatchMethod)
            ? TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod
            : mapMatchMethod;

        lock (_syncRoot)
        {
            if (TryResolveMap(screen, preferredMapName, matchingMethod, resetNavigation: false, out var point))
            {
                position = new RouteCurrentPosition(
                    RouteGraphGeometry.NormalizeMapName(preferredMapName),
                    point);
                return true;
            }

            foreach (var mapType in Enum.GetValues<MapTypes>())
            {
                var candidate = mapType.ToString();
                if (string.Equals(candidate, preferredMapName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveMap(screen, candidate, matchingMethod, resetNavigation: true, out point))
                {
                    position = new RouteCurrentPosition(candidate, point);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveMap(
        ImageRegion screen,
        string mapName,
        string matchingMethod,
        bool resetNavigation,
        out RouteGraphPoint point)
    {
        point = default;
        try
        {
            var map = MapManager.GetMap(mapName, matchingMethod);
            if (map == null)
            {
                return false;
            }

            if (resetNavigation)
            {
                Navigation.Reset();
            }

            var imagePosition = Navigation.GetPositionStable(screen, mapName, matchingMethod);
            if (!IsValidImagePosition(map, imagePosition))
            {
                return false;
            }

            point = new RouteGraphPoint(imagePosition.X, imagePosition.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidImagePosition(ISceneMap map, Point2f imagePosition)
    {
        if (imagePosition == default ||
            !float.IsFinite(imagePosition.X) ||
            !float.IsFinite(imagePosition.Y))
        {
            return false;
        }

        if (map is not SceneBaseMap sceneMap)
        {
            return true;
        }

        const float tolerance = 32f;
        return imagePosition.X >= -tolerance &&
               imagePosition.Y >= -tolerance &&
               imagePosition.X <= sceneMap.MapSize.Width + tolerance &&
               imagePosition.Y <= sceneMap.MapSize.Height + tolerance;
    }
}
