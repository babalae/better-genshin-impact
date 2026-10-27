using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 命名的共享设置集合，可挂在任意节点并跨计划复用；不包含任务结构、顺序或引用。
/// </summary>
public sealed class PuloniaSettingsProfile
{
    /// <summary>
    /// 稳定方案 ID，同时是文件名 profiles/&lt;id&gt;.json。
    /// </summary>
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = PuloniaId.NewProfileId();

    /// <summary>
    /// 方案显示名称。
    /// </summary>
    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = "新方案";

    /// <summary>
    /// 用户填写的方案说明。
    /// </summary>
    [JsonProperty("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 内容修订号，成功保存后递增，用于避免旧编辑覆盖。
    /// </summary>
    [JsonProperty("revision")]
    public long Revision { get; set; }

    /// <summary>
    /// 设置项 ID 到值的稀疏集合；未出现的设置项继续沿设置链继承。
    /// </summary>
    [JsonProperty("settings", Required = Required.DisallowNull)]
    public JObject Settings { get; set; } = new();
}
