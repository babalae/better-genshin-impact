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
using BetterGenshinImpact.Model;
using System.Linq;


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
    /// 路径追踪任务配置
    /// </summary>
    public PathingTaskConfig Config { get; set; } = new();
    
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
        
        //计算一共有多少级目录
        var level = relativePath.Count(c => c == Path.DirectorySeparatorChar);
        
        //获取每一级目录的名称
        var fileNames = relativePath.Split(Path.DirectorySeparatorChar);
        
        // 获取ConditionDefinitions采集物列表
        var conditionDefinition = ConditionDefinitions.Definitions["采集物"];
        var materialList = conditionDefinition.ObjectOptions?.ToList() ?? new List<string>();
        
        //跳过第一个目录，i从1开始,（一级目录必定不是采集物），对比每一个采集物名称 
        for (var i = 1; i < level; i++)
        {
            var materialName = fileNames[i];
            if (materialList.Contains(materialName))
            {
                return materialName;
            }
        }
        return null;
    }

    public static PathingTask? BuildFromFilePath(string filePath)
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
            TaskControl.Logger.LogError("地图追踪任务 {Name} 版本号要求 {BgiVersion} 大于当前 BetterGI 版本号 {CurrentVersion} ， 禁止运行，请更新 BetterGI 版本！", task.FileName, task.Info.BgiVersion, Global.Version);
            TaskControl.Logger.LogError("地图追踪任务 {Name} 版本号要求 {BgiVersion} 大于当前 BetterGI 版本号 {CurrentVersion} ， 禁止运行，请更新 BetterGI 版本！", task.FileName, task.Info.BgiVersion, Global.Version);
            TaskControl.Logger.LogError("地图追踪任务 {Name} 版本号要求 {BgiVersion} 大于当前 BetterGI 版本号 {CurrentVersion} ， 禁止运行，请更新 BetterGI 版本！", task.FileName, task.Info.BgiVersion, Global.Version);
            return null;
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
