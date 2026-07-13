using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class PathingTaskInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string? Author { get; set; }

    public string? Version { get; set; }

    /// <summary>
    /// 制作时 BetterGI 的版本，用于兼容性检查
    /// </summary>
    public string? BgiVersion { get; set; }

    /// <summary>
    /// 任务类型
    /// <see cref="PathingTaskType"/>
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// 排序
    /// 同个文件夹下的任务会根据 Order 排序进入调度器
    /// </summary>
    public int Order { get; set; } = 0;
    
    /// <summary>
    /// 标签，用于展示
    /// </summary>
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// 区分怪物拾取
    /// </summary>
    public bool EnableMonsterLootSplit { get; set; } = false;

    [JsonIgnore]
    public string TypeDesc => PathingTaskType.GetMsgByCode(Type);
    
    /// <summary>
    /// 地图名称
    /// </summary>
    public string MapName { get; set; } = nameof(MapTypes.Teyvat);
    
    /// <summary>
    /// 默认的地图匹配方式
    /// SIFT 老的匹配方式
    /// TemplateMatch 支持分层地图
    /// </summary>
    public string MapMatchMethod { get; set; } = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
    
    
    public List<MaterialInfo> Items { get; set; } = [];
    
}
