using System;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.Map;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoTrackPath.Model;

/// <summary>
/// 路线点
/// 坐标必须游戏内坐标系
/// </summary>
[Serializable]
public class GiPathPoint
{
    public Point Pt { get; set; }

    public Rect MatchRect { get; set; }

    public int Index { get; set; }

    public DateTime Time { get; set; }

    public string Type { get; set; } = GiPathPointType.Normal.ToString();

    public static GiPathPoint BuildFrom(Rect matchRect, int index)
    {
        var pt = MapCoordinate.Main2048ToGame(matchRect.GetCenterPoint());
        return new GiPathPoint
        {
            Pt = pt,
            MatchRect = matchRect,
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
