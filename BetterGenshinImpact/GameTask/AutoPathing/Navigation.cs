using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class Navigation
{
    private static bool _isWarmUp = false;

    private static float _prevX = -1;
    private static float _prevY = -1;

    public static void WarmUp()
    {
        if (!_isWarmUp)
        {
            MapManager.GetMap(MapTypes.Teyvat);
        }

        _isWarmUp = true;
        Reset();
    }

    public static void Reset()
    {
        (_prevX, _prevY) = (-1, -1);
    }
    
    public static void SetPrevPosition(float x, float y)
    {
        (_prevX, _prevY) = (x, y);
    }

    public static Point2f GetPosition(ImageRegion imageRegion, string mapName)
    {
        var greyMat = new Mat(imageRegion.SrcGreyMat, MapAssets.Instance.MimiMapRect);
        var p = MapManager.GetMap(mapName).GetMiniMapPosition(greyMat, _prevX, _prevY);
        if (p != default)
        {
            (_prevX, _prevY) = (p.X, p.Y);
        }
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(typeof(Navigation),
            "SendCurrentPosition", new object(), p));
        return p;
    }

    /// <summary>
    /// 稳定获取当前位置坐标，优先使用全地图匹配，适用于不需要高效率但需要高稳定性的场景
    /// </summary>
    /// <param name="imageRegion">图像区域</param>
    /// <param name="mapName"></param>
    /// <returns>当前位置坐标</returns>
    public static Point2f GetPositionStable(ImageRegion imageRegion, string mapName)
    {
        var greyMat = new Mat(imageRegion.SrcGreyMat, MapAssets.Instance.MimiMapRect);

        // 先尝试使用局部匹配
        var p =  MapManager.GetMap(mapName).GetMiniMapPosition(greyMat, _prevX, _prevY);

        // 如果局部匹配失败，再尝试全地图匹配失败
        if (p == new Point2f())
        {
            Reset();
            p = MapManager.GetMap(mapName).GetMiniMapPosition(greyMat, _prevX, _prevY);
        }
        if (p != default)
        {
            (_prevX, _prevY) = (p.X, p.Y);
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(typeof(Navigation),
            "SendCurrentPosition", new object(), p));
        return p;
    }

    public static int GetTargetOrientation(Waypoint waypoint, Point2f position)
    {
        double deltaX = waypoint.X - position.X;
        double deltaY = waypoint.Y - position.Y;
        double vectorLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (vectorLength == 0)
        {
            return 0;
        }

        // 计算向量与x轴之间的夹角（逆时针方向）
        double angle = Math.Acos(deltaX / vectorLength);
        // 如果向量在x轴下方，角度需要调整
        if (deltaY < 0)
        {
            angle = 2 * Math.PI - angle;
        }

        return (int)(angle * (180.0 / Math.PI));
    }

    public static double GetDistance(Waypoint waypoint, Point2f position)
    {
        var x1 = waypoint.X;
        var y1 = waypoint.Y;
        var x2 = position.X;
        var y2 = position.Y;
        return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    }
}