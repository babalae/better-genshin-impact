using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BetterGenshinImpact.Core.Script.Repositories;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 可编辑、可持久化的任务树节点；运行状态不写入此模型。
/// </summary>
public sealed class PuloniaTask
{
    /// <summary>
    /// 稳定节点 ID，重命名和移动节点时保持不变。
    /// </summary>
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = PuloniaId.NewTaskId();

    /// <summary>
    /// 用户可见的任务名称。
    /// </summary>
    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = "新任务";

    /// <summary>
    /// 唯一的类型判别字段，例如 group、pathing 或 builtin.mail。
    /// </summary>
    [JsonProperty("task_type", Required = Required.Always)]
    public string TaskType { get; set; } = "group";

    /// <summary>
    /// 资源定位；不参与节点身份判定。
    /// </summary>
    [JsonProperty("path")]
    public string? Path { get; set; }

    /// <summary>远程资源的明确来源与已确认仓库版本；旧 path 引用继续兼容读取。</summary>
    [JsonProperty("resource", NullValueHandling = NullValueHandling.Ignore)]
    public ScriptResourceReference? Resource { get; set; }

    /// <summary>参数与预设匹配使用的资源身份，远程来源包含仓库 ID。</summary>
    [JsonIgnore]
    public string? ResourceKey => Resource?.ScopeKey ?? Path;

    /// <summary>
    /// 创建或显式更新时固定的资源内容指纹；资源发生变化时拒绝静默执行。
    /// </summary>
    [JsonProperty("resource_version")]
    public string? ResourceVersion { get; set; }

    /// <summary>
    /// 节点自身开关；关闭父组不修改子节点开关。
    /// </summary>
    [JsonProperty("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 本节点参数；保留缺省、null、false、0 和集合的区别。
    /// </summary>
    [JsonProperty("parameters", Required = Required.DisallowNull)]
    public JObject Parameters { get; set; } = new();

    /// <summary>
    /// 子任务顺序即执行顺序，叶子任务必须为空。
    /// </summary>
    [JsonProperty("children", Required = Required.DisallowNull)]
    public List<PuloniaTask> Children { get; set; } = [];

    /// <summary>
    /// 分组完整执行子任务列表的次数；null 与 1 都表示只执行一次。
    /// </summary>
    [JsonProperty("repeat_count", NullValueHandling = NullValueHandling.Ignore)]
    public int? RepeatCount { get; set; }

    /// <summary>
    /// 选用的共享预设 ID；账号绑定可以替换选择。
    /// </summary>
    [JsonProperty("preset_id")]
    public string? PresetId { get; set; }

    /// <summary>
    /// 可继承的执行策略，未填写字段继续继承。
    /// </summary>
    [JsonProperty("policy", Required = Required.DisallowNull)]
    public PuloniaTaskPolicy Policy { get; set; } = new();

    /// <summary>
    /// 目录或计划引用的来源；引用与可编辑子任务互斥。
    /// </summary>
    [JsonProperty("source")]
    public PuloniaTaskSource? Source { get; set; }

    /// <summary>
    /// 分组为指定类型和资源提供的公共参数，避免不同能力串用配置。
    /// </summary>
    [JsonProperty("parameter_overrides", Required = Required.DisallowNull)]
    public List<PuloniaTaskParameterOverride> ParameterOverrides { get; set; } = [];

    /// <summary>
    /// 仅用于树视图，不影响执行。
    /// </summary>
    [JsonProperty("is_expanded")]
    public bool IsExpanded { get; set; } = true;
}
