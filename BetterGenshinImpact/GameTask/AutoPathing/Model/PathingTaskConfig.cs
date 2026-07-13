using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

/// <summary>
/// 路径追踪任务配置类
/// </summary>
[Serializable]
public class PathingTaskConfig
{
    /// <summary>
    /// 实时触发器配置开关列表
    /// </summary>
    public Dictionary<string, bool> RealtimeTriggers { get; set; } = new()
    {
        { "AutoPick", true },        // 自动拾取
    };
}
