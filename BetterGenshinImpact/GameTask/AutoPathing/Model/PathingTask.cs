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

public enum PathingTaskType
{
    Collect, // 采集
    Mining, // 挖矿
    Fight // 锄地
}

[Serializable]
public class PathingTaskInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 任务类型 PathingTaskType
    /// </summary>
    public string Type { get; set; } = string.Empty;

    // 任务参数/配置
    // 持续操作 切换某个角色 长E or 短E
    // 持续疾跑
    // 边跳边走
}
