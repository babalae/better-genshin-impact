using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class Navigation
{
    private static string? _lastMapMatchMethod = null;
    
    private static readonly NavigationInstance _instance = new();

    public static void WarmUp(string mapMatchMethod)
    {
        if (_lastMapMatchMethod != mapMatchMethod)
        {
            MapManager.GetMap(MapTypes.Teyvat, mapMatchMethod).WarmUp();
            _lastMapMatchMethod = mapMatchMethod;
        }

        Reset();
    }

    public static void Reset()
    {
        _instance.Reset();
    }
    
    public static void SetPrevPosition(float x, float y)
    {
        _instance.SetPrevPosition(x,y);
    }

    public static Point2f GetPosition(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        return _instance.GetPosition(imageRegion, mapName, mapMatchMethod);
    }

    /// <summary>
    /// 稳定获取当前位置坐标，优先使用全地图匹配，适用于不需要高效率但需要高稳定性的场景
    /// </summary>
    /// <param name="imageRegion">图像区域</param>
    /// <param name="mapName"></param>
    /// <param name="mapMatchMethod"></param>
    /// <returns>当前位置坐标</returns>
    public static Point2f GetPositionStable(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        return _instance.GetPositionStable(imageRegion, mapName, mapMatchMethod);
    }

    public static int GetTargetOrientation(Waypoint waypoint, Point2f position)
    {
        double angle = Math.Atan2(waypoint.Y - position.Y, waypoint.X - position.X);
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        return (int)(angle * (180.0 / Math.PI));
    }

    public static double GetDistance(Waypoint waypoint, Point2f position)
    {
        return Math.Sqrt(Math.Pow(position.X - waypoint.X, 2) + Math.Pow(position.Y - waypoint.Y, 2));
    }
}
