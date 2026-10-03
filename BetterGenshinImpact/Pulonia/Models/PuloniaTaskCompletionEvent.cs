using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 执行器提交的幂等完成事件；持久化成功后才允许推进任务。
/// </summary>
public sealed class PuloniaTaskCompletionEvent
{
    /// <summary>
    /// 对应能力可用性规则 ID。
    /// </summary>
    [JsonProperty("rule_id", Required = Required.Always)]
    public string RuleId { get; init; } = string.Empty;

    /// <summary>
    /// 稳定事件键；为空时由宿主按运行、节点和规则生成。
    /// </summary>
    [JsonProperty("event_key")]
    public string? EventKey { get; init; }

    /// <summary>
    /// 本次确认消耗的额度单位。
    /// </summary>
    [JsonProperty("units")]
    public int Units { get; init; } = 1;

    /// <summary>
    /// 实际副作用发生时间，统一使用 UTC。
    /// </summary>
    [JsonProperty("occurred_at", Required = Required.Always)]
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 支撑本次确认的结构化证据。
    /// </summary>
    [JsonProperty("evidence", Required = Required.DisallowNull)]
    public PuloniaTaskEvidence Evidence { get; init; } = new();
}
