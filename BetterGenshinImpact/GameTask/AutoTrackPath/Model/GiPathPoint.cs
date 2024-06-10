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

    /// <summary>
    /// 下个点位相对本点位的角度
    /// </summary>
    public int NextAngle { get; set; }

    /// <summary>
    /// 下个点位相对本点位的距离
    /// </summary>
    public int NextDistance { get; set; }

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
}
