using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System;
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
    /// 区分怪物拾取
    /// </summary>
    public bool EnableMonsterLootSplit { get; set; } = false;

    [JsonIgnore]
    public string TypeDesc => PathingTaskType.GetMsgByCode(Type);
    
    public string MapName { get; set; } = MapTypes.Teyvat.ToString();

    // 任务参数/配置
    // 持续操作 切换某个角色 长E or 短E
    // 持续疾跑
    // 边跳边走
}
