using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 操作意图中一条规则对具体作用域和资源的占用。
/// </summary>
public sealed class PuloniaTaskRuleReservation
{
    /// <summary>
    /// 能力内规则 ID。
    /// </summary>
    [JsonProperty("rule_id", Required = Required.Always)]
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// 账号或世界作用域键。
    /// </summary>
    [JsonProperty("scope_key", Required = Required.Always)]
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>
    /// 稳定资源或活动 ID。
    /// </summary>
    [JsonProperty("effect_key", Required = Required.Always)]
    public string EffectKey { get; set; } = string.Empty;
}
