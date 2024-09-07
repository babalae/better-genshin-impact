using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class PathingTaskInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 任务类型
    /// <see cref="PathingTaskType"/>
    /// </summary>
    public string Type { get; set; } = string.Empty;

    [JsonIgnore]
    public string TypeDesc => PathingTaskType.GetMsgByCode(Type);

    // 任务参数/配置
    // 持续操作 切换某个角色 长E or 短E
    // 持续疾跑
    // 边跳边走
}
