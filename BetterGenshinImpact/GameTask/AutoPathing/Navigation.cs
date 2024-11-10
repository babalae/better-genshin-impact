using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class Navigation
{
    private static bool _isWarmUp = false;

    public static void WarmUp()
    {
        if (!_isWarmUp)
        {
            TaskControl.Logger.LogInformation("地图特征点加载中，由于体积较大，首次加载速度较慢，请耐心等待...");
            EntireMap.Instance.GetFeatureMatcher();
            TaskControl.Logger.LogInformation("地图特征点加载完成！");
        }
        _isWarmUp = true;
        Reset();
    }

    public static void Reset()
    {
        EntireMap.Instance.SetPrevPosition(-1, -1);
    }

    public static Point2f GetPosition(ImageRegion imageRegion)
    {
        var greyMat = new Mat(imageRegion.SrcGreyMat, MapAssets.Instance.MimiMapRect);
        var p = EntireMap.Instance.GetMiniMapPositionByFeatureMatch(greyMat);

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
