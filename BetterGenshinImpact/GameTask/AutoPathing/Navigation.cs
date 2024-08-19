using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Common;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using BetterGenshinImpact.GameTask.Model.Area;


namespace BetterGenshinImpact.GameTask.AutoPathing;
internal class Navigation
{

    internal static Point2f GetPosition(ImageRegion imageRegion)
    {
        var greyMat = new Mat(imageRegion.SrcGreyMat, new Rect(62, 19, 212, 212));
        return EntireMap.Instance.GetMiniMapPositionByFeatureMatch(greyMat);
    }

    internal static int GetTargetOrientation(Waypoint waypoint, Point2f position)
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

    internal static double GetDistance(Waypoint waypoint, Point2f position)
    {
        var x1 = waypoint.X;
        var y1 = waypoint.Y;
        var x2 = position.X;
        var y2 = position.Y;
        return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    }
}