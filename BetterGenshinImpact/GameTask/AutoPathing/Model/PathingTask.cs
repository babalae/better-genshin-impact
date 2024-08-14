using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class PathingTask
{
    public PathingTaskInfo? Info { get; set; }
    public List<Waypoint> Waypoints { get; set; } = [];
}


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

[Serializable]
public class PathingTaskInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

}