using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 任务计划编辑器的界面状态（plan-view-state.json），与 plan-order.json 同级；
/// 不属于任何计划，不进入运行快照或执行记录，也不改变计划修订。
/// </summary>
public sealed class PuloniaTaskPlanViewState
{
    /// <summary>
    /// 当前支持的格式版本。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 文件格式版本。
    /// </summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 按计划 ID 记录已折叠的任务节点 ID；未记录的节点默认展开，已不存在的计划或节点在读取时忽略。
    /// </summary>
    [JsonProperty("collapsed_task_ids", Required = Required.DisallowNull)]
    public Dictionary<string, List<string>> CollapsedTaskIds { get; set; } = new(StringComparer.Ordinal);
}
