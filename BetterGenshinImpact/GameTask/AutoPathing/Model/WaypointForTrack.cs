using BetterGenshinImpact.GameTask.Common.Map;
using System;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class WaypointForTrack : Waypoint
{
    // 原神坐标系
    public double GameX { get; set; }

    public double GameY { get; set; }

    // 全地图图像坐标系
    public double MatX { get; set; }

    public double MatY { get; set; }

    public WaypointForTrack(Waypoint waypoint)
    {
        Type = waypoint.Type;
        MoveMode = waypoint.MoveMode;
        Action = waypoint.Action;
        GameX = waypoint.X;
        GameY = waypoint.Y;
        // 坐标系转换
        (MatX, MatY) = MapCoordinate.GameToMain2048(waypoint.X, waypoint.Y);
        X = MatX;
        Y = MatY;
    }
}
