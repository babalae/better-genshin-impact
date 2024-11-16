using System;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class Waypoint
{
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>
    /// <see cref="WaypointType"/>
    /// </summary>
    public string Type { get; set; } = WaypointType.Path.Code;

    /// <summary>
    /// <see cref="MoveModeEnum"/>
    /// </summary>
    public string MoveMode { get; set; } = MoveModeEnum.Walk.Code;

    /// <summary>
    /// <see cref="ActionEnum"/>
    /// </summary>
    public string? Action { get; set; }
    
    public string? ActionParams { get; set; }
}
