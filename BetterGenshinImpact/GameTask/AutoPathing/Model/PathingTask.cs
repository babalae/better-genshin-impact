using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly JsonSerializerOptions CompactJsonOptions = new(PathRecorder.JsonOptions)
    {
        WriteIndented = true
    };

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

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

    public List<PathingTask> SplitTasks()
    {
        var result = new List<PathingTask>();
        var tempList = new List<Waypoint>();

        if (Positions == null || Positions.Count == 0) return result;

        foreach (var p in Positions)
        {
            if (p == null) continue;

            if (p.Type == "teleport" && tempList.Count > 0)
            {
                var clone = (PathingTask)this.MemberwiseClone();
                clone.Positions = tempList;
                result.Add(clone);
                tempList = new List<Waypoint>();
            }

            tempList.Add(p);
        }

        if (tempList.Count > 0)
        {
            var clone = (PathingTask)this.MemberwiseClone();
            clone.Positions = tempList;
            result.Add(clone);
        }

        return result;
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
        var json = ToJsonString();
        File.WriteAllText(filePath, json, new UTF8Encoding(false));
    }

    public string ToJsonString()
    {
        var root = JsonSerializer.SerializeToNode(this, PathRecorder.JsonOptions) as JsonObject;
        if (root == null)
        {
            return JsonSerializer.Serialize(this, PathRecorder.JsonOptions);
        }

        RemoveDefaultJsonFields(root);
        return root.ToJsonString(CompactJsonOptions);
    }

    private static void RemoveDefaultJsonFields(JsonObject root)
    {
        if (root["info"] is JsonObject info)
        {
            RemoveEmptyArray(info, "authors");
            RemoveIfString(info, "version", "1.0");
            RemoveIfString(info, "bgi_version", Global.Version);
            RemoveIfString(info, "type", "");
            RemoveIfString(info, "type", "collect");
            RemoveIfNumber(info, "order", 0);
            RemoveEmptyArray(info, "tags");
            RemoveIfBoolean(info, "enable_monster_loot_split", false);
            RemoveIfString(info, "map_name", nameof(MapTypes.Teyvat));
            RemoveIfString(info, "map_match_method", TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod);
            RemoveEmptyArray(info, "items");

            if (info.Count == 0)
            {
                root.Remove("info");
            }
        }

        if (IsDefaultConfig(root["config"] as JsonObject))
        {
            root.Remove("config");
        }

        if (IsDefaultFarmingInfo(root["farming_info"] as JsonObject))
        {
            root.Remove("farming_info");
        }

        if (root["positions"] is JsonArray positions)
        {
            foreach (var position in positions.OfType<JsonObject>())
            {
                RemoveDefaultWaypointFields(position);
            }
        }
    }

    private static bool IsDefaultConfig(JsonObject? config)
    {
        if (config == null)
        {
            return true;
        }

        if (config.Count == 0)
        {
            return true;
        }

        if (config.Count != 1 || config["realtime_triggers"] is not JsonObject triggers)
        {
            return false;
        }

        return triggers.Count == 1
               && triggers["AutoPick"]?.GetValue<bool>() == true;
    }

    private static bool IsDefaultFarmingInfo(JsonObject? farmingInfo)
    {
        if (farmingInfo == null)
        {
            return true;
        }

        return IsBoolean(farmingInfo, "allow_farming_count", false)
               && IsNumber(farmingInfo, "normal_mob_count", 0)
               && IsNumber(farmingInfo, "elite_mob_count", 0)
               && IsString(farmingInfo, "primary_target", "")
               && IsNumber(farmingInfo, "duration_seconds", 0)
               && IsString(farmingInfo, "elite_details", "")
               && IsNumber(farmingInfo, "total_mora", 0);
    }

    private static void RemoveDefaultWaypointFields(JsonObject waypoint)
    {
        RemoveIfString(waypoint, "action_params", "");
        RemoveEmptyArray(waypoint, "items");

        if (waypoint["point_ext_params"] is not JsonObject extParams)
        {
            return;
        }

        RemoveIfString(extParams, "description", "");
        RemoveIfString(extParams, "monster_tag", "");
        RemoveIfBoolean(extParams, "enable_monster_loot_split", false);

        if (extParams["misidentification"] is JsonObject misidentification)
        {
            RemoveIfStringArray(misidentification, "type", "unrecognized");
            RemoveIfString(misidentification, "handling_mode", "previousDetectedPoint");
            RemoveIfNumber(misidentification, "arrival_time", 0);

            if (misidentification.Count == 0)
            {
                extParams.Remove("misidentification");
            }
        }

        if (extParams.Count == 0)
        {
            waypoint.Remove("point_ext_params");
        }
    }

    private static void RemoveEmptyArray(JsonObject obj, string propertyName)
    {
        if (obj[propertyName] is JsonArray array && array.Count == 0)
        {
            obj.Remove(propertyName);
        }
    }

    private static void RemoveIfString(JsonObject obj, string propertyName, string value)
    {
        if (IsString(obj, propertyName, value))
        {
            obj.Remove(propertyName);
        }
    }

    private static void RemoveIfNumber(JsonObject obj, string propertyName, double value)
    {
        if (IsNumber(obj, propertyName, value))
        {
            obj.Remove(propertyName);
        }
    }

    private static void RemoveIfBoolean(JsonObject obj, string propertyName, bool value)
    {
        if (IsBoolean(obj, propertyName, value))
        {
            obj.Remove(propertyName);
        }
    }

    private static void RemoveIfStringArray(JsonObject obj, string propertyName, params string[] values)
    {
        if (obj[propertyName] is JsonArray array
            && array.Count == values.Length
            && array.Select(i => i?.GetValue<string>()).SequenceEqual(values))
        {
            obj.Remove(propertyName);
        }
    }

    private static bool IsString(JsonObject obj, string propertyName, string value)
    {
        return obj[propertyName]?.GetValue<string>() == value;
    }

    private static bool IsNumber(JsonObject obj, string propertyName, double value)
    {
        return obj[propertyName]?.GetValue<double>() == value;
    }

    private static bool IsBoolean(JsonObject obj, string propertyName, bool value)
    {
        return obj[propertyName]?.GetValue<bool>() == value;
    }
}
