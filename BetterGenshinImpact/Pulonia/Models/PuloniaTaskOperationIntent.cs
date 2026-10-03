using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 可能产生副作用的节点在执行前写入的持久化操作意图。
/// </summary>
public sealed class PuloniaTaskOperationIntent
{
    /// <summary>
    /// 运行 ID。
    /// </summary>
    [JsonProperty("run_id", Required = Required.Always)]
    public Guid RunId { get; set; }

    /// <summary>
    /// 快照中的节点地址。
    /// </summary>
    [JsonProperty("task_address", Required = Required.Always)]
    public string TaskAddress { get; set; } = string.Empty;

    /// <summary>
    /// 本次节点需要确认的规则 ID。
    /// </summary>
    [JsonProperty("rule_ids", Required = Required.DisallowNull)]
    public List<string> RuleIds { get; set; } = [];

    /// <summary>
    /// 本次意图实际占用的作用域与资源规则。
    /// </summary>
    [JsonProperty("reservations", Required = Required.DisallowNull)]
    public List<PuloniaTaskRuleReservation> Reservations { get; set; } = [];

    /// <summary>
    /// 意图成功落盘的 UTC 时间。
    /// </summary>
    [JsonProperty("created_at", Required = Required.Always)]
    public DateTimeOffset CreatedAt { get; set; }
}
