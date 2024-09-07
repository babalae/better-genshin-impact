using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Group;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

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
}
