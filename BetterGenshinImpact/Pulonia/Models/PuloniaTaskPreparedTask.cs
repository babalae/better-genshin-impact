using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 运行准备生成的只读执行节点，保留来源定位但不把执行行为写进模型。
/// </summary>
public sealed class PuloniaTaskPreparedTask
{
    /// <summary>
    /// 私有的参数副本，避免调用者通过 JToken 修改运行配置。
    /// </summary>
    private readonly string _parametersJson;

    /// <summary>
    /// 私有的策略副本。
    /// </summary>
    private readonly string _policyJson;

    /// <summary>
    /// 原计划 ID。
    /// </summary>
    [JsonProperty("plan_id")]
    public string PlanId { get; }

    /// <summary>
    /// 展开节点 ID；目录资源使用相对路径生成的稳定 ID。
    /// </summary>
    [JsonProperty("task_id")]
    public string TaskId { get; }

    /// <summary>
    /// 编辑树中的来源节点 ID，目录展开项定位到其引用组。
    /// </summary>
    [JsonProperty("source_task_id")]
    public string SourceTaskId { get; }

    /// <summary>
    /// 包含计划调用路径和分组执行轮次的地址，不依赖名称或数组下标。
    /// </summary>
    [JsonProperty("task_address")]
    public string TaskAddress { get; }

    /// <summary>
    /// 准备时固定的名称。
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; }

    /// <summary>
    /// 节点类型。
    /// </summary>
    [JsonProperty("task_type")]
    public string TaskType { get; }

    /// <summary>
    /// 含祖先开关的有效启用状态。
    /// </summary>
    [JsonProperty("is_enabled")]
    public bool IsEnabled { get; }

    /// <summary>
    /// 解析后的资源绝对路径。
    /// </summary>
    [JsonProperty("path")]
    public string? Path { get; }

    /// <summary>
    /// 资源内容 SHA-256 或目录清单指纹；不代表资源已经缓存。
    /// </summary>
    [JsonProperty("resource_version")]
    public string? ResourceVersion { get; }

    /// <summary>
    /// 有效参数的防御性副本；修改返回值不影响运行树。
    /// </summary>
    [JsonProperty("parameters")]
    public JObject Parameters => PuloniaTaskJson.Read<JObject>(_parametersJson);

    /// <summary>
    /// 每个顶层参数的最终来源。
    /// </summary>
    [JsonProperty("parameter_sources")]
    public IReadOnlyDictionary<string, string> ParameterSources { get; }

    /// <summary>
    /// 有效策略的防御性副本。
    /// </summary>
    [JsonProperty("policy")]
    public PuloniaTaskPolicy Policy => PuloniaTaskJson.Read<PuloniaTaskPolicy>(_policyJson);

    /// <summary>
    /// 展开后的只读子树，顺序与用户编排一致。
    /// </summary>
    [JsonProperty("children")]
    public IReadOnlyList<PuloniaTaskPreparedTask> Children { get; }

    /// <summary>
    /// 固定本次准备数据，所有可变对象均复制后保存。
    /// </summary>
    [JsonConstructor]
    internal PuloniaTaskPreparedTask(string planId, string taskId, string sourceTaskId, string taskAddress,
        string name, string taskType, bool isEnabled, string? path, string? resourceVersion, JObject parameters,
        IDictionary<string, string> parameterSources, PuloniaTaskPolicy policy,
        IEnumerable<PuloniaTaskPreparedTask> children)
    {
        PlanId = planId;
        TaskId = taskId;
        SourceTaskId = sourceTaskId;
        TaskAddress = taskAddress;
        Name = name;
        TaskType = taskType;
        IsEnabled = isEnabled;
        Path = path;
        ResourceVersion = resourceVersion;
        _parametersJson = PuloniaTaskJson.Write(parameters);
        _policyJson = PuloniaTaskJson.Write(policy);
        ParameterSources = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(parameterSources));
        Children = System.Array.AsReadOnly(children.ToArray());
    }
}
