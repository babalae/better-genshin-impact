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
    /// <summary>
    /// 实际存储的文件名
    /// </summary>
    [JsonIgnore]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 实际存储的文件路径
    /// </summary>
    [JsonIgnore]
    public string FullPath { get; set; } = string.Empty;

    public PathingTaskInfo Info { get; set; } = new();
    public List<Waypoint> Positions { get; set; } = [];

    public static PathingTask BuildFromFilePath(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var task = JsonSerializer.Deserialize<PathingTask>(json, PathRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize PathingTask");
        task.FileName = Path.GetFileName(filePath);
        task.FullPath = filePath;
        return task;
    }

    public static PathingTask BuildFromJson(string json)
    {
        var task = JsonSerializer.Deserialize<PathingTask>(json, PathRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize PathingTask");
        return task;
    }

    public void SaveToFile(string filePath)
    {
        var json = JsonSerializer.Serialize(this, PathRecorder.JsonOptions);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }
}
