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

    public static GiPathPoint BuildFrom(Rect matchRect, int index)
    {
        var pt = MapCoordinate.Main1024ToGame(matchRect.GetCenterPoint());
        return new GiPathPoint
        {
            Pt = pt,
            MatchRect = new Rect(pt, matchRect.Size),
            Index = index,
            Time = DateTime.Now
        };
    }
}
