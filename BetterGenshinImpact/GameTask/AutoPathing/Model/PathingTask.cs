using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Group;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class PathingTask
{
    public PathingTaskInfo Info { get; set; } = new();
    public List<Waypoint> Waypoints { get; set; } = [];

    public static PathingTask BuildFromFilePath(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<PathingTask>(json, KeyMouseRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize PathingTask");
    }
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

    [JsonIgnore]
    public string TypeDesc => PathingTaskExtensions.TypeDescriptions[Type];

    // 任务参数/配置
    // 持续操作 切换某个角色 长E or 短E
    // 持续疾跑
    // 边跳边走
}

public class PathingTaskExtensions
{
    public static readonly Dictionary<string, string> TypeDescriptions = new()
    {
        { PathingTaskType.Collect.ToString(), "采集" },
        { PathingTaskType.Mining.ToString(), "挖矿" },
        { PathingTaskType.Fight.ToString(), "战斗" },
    };
}
