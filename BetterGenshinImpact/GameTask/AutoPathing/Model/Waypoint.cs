using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;
[Serializable]
public class Waypoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public WaypointType WaypointType { get; set; }
    public MoveType MoveType { get; set; }
    public ActionType? ActionType { get; set; }
}

public enum WaypointType
{
    Transit,
    Target,
    Teleport,
}

public enum MoveType
{
    Walk,
    Fly,
    Jump,
    Swim,
}

public enum ActionType
{
    StopFlying,
}
