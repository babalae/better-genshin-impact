using Newtonsoft.Json;
using BetterGenshinImpact.Core.Script.Repositories;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 计划引用或目录引用的定位信息。
/// </summary>
public sealed class PuloniaTaskSource
{
    /// <summary>
    /// 来源类型：plan 或 directory。
    /// </summary>
    [JsonProperty("kind", Required = Required.Always)]
    public string Kind { get; set; } = "directory";

    /// <summary>
    /// 计划引用目标 ID。
    /// </summary>
    [JsonProperty("plan_id")]
    public string? PlanId { get; set; }

    /// <summary>
    /// 目录位置，支持运行准备时提供的路径变量。
    /// </summary>
    [JsonProperty("path")]
    public string? Path { get; set; }

    /// <summary>目录引用的已确认仓库定位，文件清单从该版本展开。</summary>
    [JsonProperty("resource", NullValueHandling = NullValueHandling.Ignore)]
    public ScriptResourceReference? Resource { get; set; }

    /// <summary>
    /// 目录中的资源类型；原型支持 pathing 和 keymouse JSON 文件。
    /// </summary>
    [JsonProperty("task_type")]
    public string? TaskType { get; set; }

    /// <summary>
    /// 是否包含子目录；始终跳过重解析点。
    /// </summary>
    [JsonProperty("recursive")]
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// 可选的预期目录清单指纹，发生变化时拒绝静默替换。
    /// </summary>
    [JsonProperty("version")]
    public string? Version { get; set; }
}
