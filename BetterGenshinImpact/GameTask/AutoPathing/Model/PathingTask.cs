using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using BetterGenshinImpact.Core.Script.Utils;
using BetterGenshinImpact.GameTask.FarmingPlan;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

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
    
    /// <summary>
    /// 锄地信息
    /// </summary>
    public FarmingSession  FarmingInfo { get; set; } = new();
    public List<Waypoint> Positions { get; set; } = [];

    public bool HasAction(string actionName)
    {
        return Positions.Exists(p => p.Action == actionName);
    }

    /// <summary>
    /// 获取采集物名称
    /// </summary>
    /// <returns></returns>
    public string? GetMaterialName()
    {
        if (string.IsNullOrWhiteSpace(FullPath))
        {
            return null;
        }

        // 获取 MapPathingViewModel.PathJsonPath
        var basePath = MapPathingViewModel.PathJsonPath;

        // 获取 FullPath 相对于 basePath 的相对路径
        var relativePath = Path.GetRelativePath(basePath, FullPath);

        // 分割相对路径，获取第一个文件夹名称
        var firstFolder = relativePath.Split(Path.DirectorySeparatorChar)[0];

        return firstFolder;
    }

    public static PathingTask BuildFromFilePath(string filePath)
    {
        //var json = File.ReadAllText(filePath);
        var task = JsonSerializer.Deserialize<PathingTask>(JsonMerger.getMergePathingJson(filePath), PathRecorder.JsonOptions) ?? throw new Exception("Failed to deserialize PathingTask");
        task.FileName = Path.GetFileName(filePath);
        task.FullPath = filePath;
        //添加区分怪物拾取标志
        foreach (var taskPosition in task.Positions)
        {
            taskPosition.PointExtParams.EnableMonsterLootSplit = task.Info.EnableMonsterLootSplit;
        }
        // 比较版本号大小 BgiVersion
        if (!string.IsNullOrWhiteSpace(task.Info.BgiVersion) && Global.IsNewVersion(task.Info.BgiVersion))
        {
            TaskControl.Logger.LogError("地图追踪任务 {Name} 版本号要求 {BgiVersion} 大于当前 BetterGI 版本号 {CurrentVersion} ， 脚本可能无法正常工作，请更新 BetterGI 版本！", task.FileName, task.Info.BgiVersion, Global.Version);
        }
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
