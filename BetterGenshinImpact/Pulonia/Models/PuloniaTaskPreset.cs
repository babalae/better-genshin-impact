using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 可选的共享参数预设，不依赖 WPF 编辑器。
/// </summary>
public sealed class PuloniaTaskPreset
{
    /// <summary>
    /// 稳定预设 ID。
    /// </summary>
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 预设显示名称。
    /// </summary>
    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = "新预设";

    /// <summary>
    /// 内容修订号，成功保存后递增，用于避免旧编辑覆盖。
    /// </summary>
    [JsonProperty("revision")]
    public long Revision { get; set; }

    /// <summary>
    /// 预设对应的任务类型。
    /// </summary>
    [JsonProperty("task_type", Required = Required.Always)]
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// 脚本等特定资源的稳定标识。
    /// </summary>
    [JsonProperty("resource_id")]
    public string? ResourceId { get; set; }

    /// <summary>
    /// 对应的参数格式版本。
    /// </summary>
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// 预设参数，保留原始 JSON 类型。
    /// </summary>
    [JsonProperty("values", Required = Required.DisallowNull)]
    public JObject Values { get; set; } = new();
}
