using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class PathingTask
{
    public PathingTaskInfo Info { get; set; } = new();
    public List<Waypoint> Positions { get; set; } = [];

    public static PathingTask BuildFromFilePath(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<PathingTask>(json, PathRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize PathingTask");
    }

    public void SaveToFile(string filePath)
    {
        var json = JsonSerializer.Serialize(this, PathRecorder.JsonOptions);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }
}
