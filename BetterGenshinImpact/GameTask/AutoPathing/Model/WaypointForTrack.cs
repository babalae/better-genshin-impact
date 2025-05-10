using BetterGenshinImpact.GameTask.Common.Map;
using System;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.Map.Maps;

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
    
    public string MapName { get; set; }
    //异常识别
    public Misidentification Misidentification { get; set; } = new();

    /// <summary>
    /// 存在 combat_script 的 action 的话，这个值会存在
    /// </summary>
    public CombatScript? CombatScript { get; set; }

    /// <summary>
    /// LogOutput 特有
    /// </summary>
    public string? LogInfo { get; set; }

    public WaypointForTrack(Waypoint waypoint, string mapName)
    {
        Type = waypoint.Type;
        MoveMode = waypoint.MoveMode;
        Action = waypoint.Action;
        ActionParams = waypoint.ActionParams;
        GameX = waypoint.X;
        GameY = waypoint.Y;
        MapName = mapName;
        // 坐标系转换
        (MatX, MatY) = MapManager.GetMap(mapName).ConvertGenshinMapCoordinatesToImageCoordinates((float)waypoint.X, (float)waypoint.Y);
        X = MatX;
        Y = MatY;
        if (waypoint.Action == ActionEnum.CombatScript.Code)
        {
            if (waypoint.ActionParams is { } str)
            {
                CombatScript = CombatScriptParser.ParseContext(str, false);
            }
        }
        else if (waypoint.Action == ActionEnum.LogOutput.Code)
        {
            if (waypoint.ActionParams is not null)
            {
                LogInfo = waypoint.ActionParams;
            }
        }
        // 非必要不需要在此处新增变量解析，建议把耗时低的解析放到 Handler 中
    }
}