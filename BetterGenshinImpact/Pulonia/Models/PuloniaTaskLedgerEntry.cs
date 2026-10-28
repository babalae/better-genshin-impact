using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 当前状态文件中的一条路线 CD 成功记录（每个作用域与文件只保留最新一条）。
/// 写入账本后视为不可变：已提交的状态与查询结果共用同一对象，只能整条替换或删除，不能原地修改。
/// </summary>
public sealed class PuloniaTaskLedgerEntry
{
    /// <summary>
    /// 幂等事件键。
    /// </summary>
    [JsonProperty("event_key", Required = Required.Always)]
    public string EventKey { get; set; } = string.Empty;

    /// <summary>
    /// 采集记录所属世界的作用域键，形如 <c>cn/world/{身份}</c>：未切换账号时身份为 <c>default</c>，切换到指定账号时为核验后的 UID。
    /// </summary>
    [JsonProperty("scope_key", Required = Required.Always)]
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>
    /// 资源键，形如 <c>pathing.file:{文件身份}</c>。
    /// </summary>
    [JsonProperty("effect_key", Required = Required.Always)]
    public string EffectKey { get; set; } = string.Empty;

    /// <summary>
    /// 固定为 <c>file_success</c>；已删除的额度框架留下的字段，不参与判定，待下一次状态格式变更时删除。
    /// </summary>
    [JsonProperty("window_key", Required = Required.Always)]
    public string WindowKey { get; set; } = string.Empty;

    /// <summary>
    /// 固定为 1；已删除的额度框架留下的字段，不参与判定，待下一次状态格式变更时删除。
    /// </summary>
    [JsonProperty("units")]
    public int Units { get; set; } = 1;

    /// <summary>
    /// 实际副作用发生时间，使用 UTC。
    /// </summary>
    [JsonProperty("occurred_at", Required = Required.Always)]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// 按记录时规则计算的下次可执行时间；一次性和不检查时为空。运行判定按当前规则重算，CD 总览用它展示。
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
