using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 计划中的账号选择与节点预设替换，不存储登录凭据。
/// </summary>
public sealed class PuloniaTaskAccountBinding
{
    /// <summary>
    /// 外部账号资料的稳定 ID。
    /// </summary>
    [JsonProperty("account_id", Required = Required.Always)]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// 是否参与该计划。
    /// </summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 以本计划节点 ID 为键、预设 ID 为值，替换预设选择而非增加覆盖层。
    /// </summary>
    [JsonProperty("preset_selections", Required = Required.DisallowNull)]
    public Dictionary<string, string> PresetSelections { get; set; } = new();
}
