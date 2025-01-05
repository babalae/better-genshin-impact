using BetterGenshinImpact.GameTask.Common.Map;
using System;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

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
    
    /// <summary>
    /// 存在 combat_script 的 action 的话，这个值会存在
    /// </summary>
    public CombatScript? CombatScript { get; set; }

    public WaypointForTrack(Waypoint waypoint)
    {
        Type = waypoint.Type;
        MoveMode = waypoint.MoveMode;
        Action = waypoint.Action;
        ActionParams = waypoint.ActionParams;
        GameX = waypoint.X;
        GameY = waypoint.Y;
        // 坐标系转换
        (MatX, MatY) = MapCoordinate.GameToMain2048(waypoint.X, waypoint.Y);
        X = MatX;
        Y = MatY;
        
        if (waypoint.Action == ActionEnum.CombatScript.Code && waypoint.ActionParams is { } str)
        {
            CombatScript = CombatScriptParser.ParseContext(str, false);
        }
    }
}
