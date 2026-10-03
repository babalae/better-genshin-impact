using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 当前状态文件中的一条已确认 CD 或额度事实。
/// </summary>
public sealed class PuloniaTaskLedgerEntry
{
    /// <summary>
    /// 幂等事件键。
    /// </summary>
    [JsonProperty("event_key", Required = Required.Always)]
    public string EventKey { get; set; } = string.Empty;

    /// <summary>
    /// 服务器与账号或世界拥有者组成的作用域键。
    /// </summary>
    [JsonProperty("scope_key", Required = Required.Always)]
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>
    /// 稳定资源或活动 ID。
    /// </summary>
    [JsonProperty("effect_key", Required = Required.Always)]
    public string EffectKey { get; set; } = string.Empty;

    /// <summary>
    /// 规则对应的周期窗口；滚动 CD 使用 rolling。
    /// </summary>
    [JsonProperty("window_key", Required = Required.Always)]
    public string WindowKey { get; set; } = string.Empty;

    /// <summary>
    /// 已确认消耗的额度单位。
    /// </summary>
    [JsonProperty("units")]
    public int Units { get; set; } = 1;

    /// <summary>
    /// 实际副作用发生时间，使用 UTC。
    /// </summary>
    [JsonProperty("occurred_at", Required = Required.Always)]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// 滚动 CD 的下次可执行时间；周期额度为空。
    /// </summary>
    [JsonProperty("next_eligible_at")]
    public DateTimeOffset? NextEligibleAt { get; set; }

    /// <summary>
    /// 产生此事实的节点尝试 ID。
    /// </summary>
    [JsonProperty("task_run_id", Required = Required.Always)]
    public string TaskRunId { get; set; } = string.Empty;

    /// <summary>
    /// 完成事实的结构化证据。
    /// </summary>
    [JsonProperty("evidence", Required = Required.DisallowNull)]
    public PuloniaTaskEvidence Evidence { get; set; } = new();
}
