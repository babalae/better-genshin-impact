using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 能力声明的 CD 或周期额度规则。
/// </summary>
public sealed class PuloniaTaskAvailabilityRule
{
    /// <summary>
    /// 能力内稳定的规则 ID，完成事件通过它关联额度。
    /// </summary>
    [JsonProperty("rule_id", Required = Required.Always)]
    public string RuleId { get; init; } = string.Empty;

    /// <summary>
    /// 跨计划共享的稳定资源或活动 ID。
    /// </summary>
    [JsonProperty("effect_key", Required = Required.Always)]
    public string EffectKey { get; init; } = string.Empty;

    /// <summary>
    /// 规则类型。
    /// </summary>
    [JsonProperty("kind", Required = Required.Always)]
    public PuloniaTaskAvailabilityKind Kind { get; init; }

    /// <summary>
    /// 规则按账号还是世界隔离。
    /// </summary>
    [JsonProperty("scope", Required = Required.Always)]
    public PuloniaTaskScopeKind Scope { get; init; } = PuloniaTaskScopeKind.Account;

    /// <summary>
    /// 周期额度类型；仅 ResetQuota 使用。
    /// </summary>
    [JsonProperty("reset_period")]
    public PuloniaTaskResetPeriod? ResetPeriod { get; init; }

    /// <summary>
    /// 每个周期允许消耗的单位数；仅 ResetQuota 使用。
    /// </summary>
    [JsonProperty("quota")]
    public int Quota { get; init; } = 1;

    /// <summary>
    /// 滚动冷却秒数；仅 RollingCooldown 使用。
    /// </summary>
    [JsonProperty("cooldown_seconds")]
    public double? CooldownSeconds { get; init; }

    /// <summary>
    /// 服务器本地重置小时，首版默认使用 4 点。
    /// </summary>
    [JsonProperty("reset_hour")]
    public int ResetHour { get; init; } = 4;

    /// <summary>
    /// 可选的 C# operation 过滤值，用于一个执行器内的受控验收能力。
    /// </summary>
    [JsonProperty("operation")]
    public string? Operation { get; init; }

    /// <summary>
    /// 判断规则是否适用于当前节点参数。
    /// </summary>
    public bool AppliesTo(Newtonsoft.Json.Linq.JObject parameters)
        => Operation is null || string.Equals(parameters.Value<string>("operation"), Operation,
            StringComparison.Ordinal);
}
