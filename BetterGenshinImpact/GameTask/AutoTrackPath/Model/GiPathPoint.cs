using BetterGenshinImpact.GameTask.Common.Map;
using OpenCvSharp;
using System;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoTrackPath.Model;

/// <summary>
/// 路线点
/// 坐标必须游戏内坐标系
/// </summary>
[Serializable]
public class GiPathPoint
{
    public Point2f Pt { get; set; }

    public Point2f MatchPt { get; set; }

    public int Index { get; set; }

    public DateTime Time { get; set; }

    public string Type { get; set; } = GiPathPointType.Normal.ToString();

    public static GiPathPoint BuildFrom(Point2f point, int index)
    {
        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        var pt = MapManager.GetMap(MapTypes.Teyvat, matchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(point);
        return new GiPathPoint
        {
            Pt = pt,
            MatchPt = point,
            Index = index,
            Time = DateTime.Now
        };
    }

    public static bool IsKeyPoint(GiPathPoint giPathPoint)
    {
        if (giPathPoint.Type == GiPathPointType.KeyPoint.ToString()
            || giPathPoint.Type == GiPathPointType.Fighting.ToString()
            || giPathPoint.Type == GiPathPointType.Collection.ToString())
        {
            return true;
        }
        return false;
    }
}

public enum GiPathPointType
{
    Normal, // 普通点
    KeyPoint, // 关键点
    Fighting, // 战斗点
    Collection, // 采集点
}
