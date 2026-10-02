using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 保存任务计划列表的界面顺序；该顺序不属于任何单个计划的执行内容。
/// </summary>
public sealed class PuloniaTaskPlanCatalog
{
    /// <summary>
    /// 当前支持的目录格式版本。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 目录文件格式版本。
    /// </summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 按界面显示顺序排列的稳定计划 ID；已不存在的 ID 会在读取时忽略。
    /// </summary>
    [JsonProperty("plan_order", Required = Required.DisallowNull)]
    public List<string> PlanOrder { get; set; } = [];
}
