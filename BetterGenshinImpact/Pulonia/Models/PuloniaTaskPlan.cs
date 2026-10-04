using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一份任务计划的元信息、任务树和入口配置。
/// </summary>
public sealed class PuloniaTaskPlan
{
    /// <summary>
    /// 当前支持的文件格式版本。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 稳定计划 ID，同时作为存储文件名。
    /// </summary>
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 文件格式版本，未知版本不能自动降级读取。
    /// </summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 内容修订号；新计划为 0，成功保存后递增。
    /// </summary>
    [JsonProperty("revision", Required = Required.Always)]
    public long Revision { get; set; }

    /// <summary>
    /// 计划显示名称，修改时不改变 ID。
    /// </summary>
    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = "新计划";

    /// <summary>
    /// 用户填写的计划说明。
    /// </summary>
    [JsonProperty("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 唯一根节点，类型固定为 group。
    /// </summary>
    [JsonProperty("root_task", Required = Required.Always)]
    public PuloniaTask RootTask { get; set; } = new() { Name = "根分组" };

    /// <summary>
    /// 计划内定时与热键配置，运行状态和调度游标不写入此处。
    /// </summary>
    [JsonProperty("triggers", Required = Required.DisallowNull)]
    public List<PuloniaTaskTrigger> Triggers { get; set; } = [];

    /// <summary>
    /// 账号绑定，列表顺序就是将来的账号执行顺序。
    /// </summary>
    [JsonProperty("accounts", Required = Required.DisallowNull)]
    public List<PuloniaTaskAccountBinding> Accounts { get; set; } = [];
}
