using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// state.json 中的调度游标和最后一次准入结果，与排队请求在同一次状态替换提交。
/// </summary>
public sealed class PuloniaTaskTriggerState
{
    /// <summary>所属计划 ID。</summary>
    public string PlanId { get; set; } = string.Empty;
    /// <summary>触发器 ID。</summary>
    public string TriggerId { get; set; } = string.Empty;
    /// <summary>触发配置的生效时间；编辑后重建待触发时间，不沿用旧等待条件。</summary>
    public DateTimeOffset TriggerActivatedAtUtc { get; set; }
    /// <summary>已经处理的最晚日程时刻；时钟回拨不得重放。</summary>
    public DateTimeOffset? LastOccurrenceUtc { get; set; }
    /// <summary>下一个日程时刻；热键入口或未来无有效时间时为空。</summary>
    public DateTimeOffset? NextOccurrenceUtc { get; set; }
    /// <summary>最近一次入队的运行 ID，JSON 名为 last_run_id。</summary>
    [JsonProperty("last_run_id")]
    public string? LastRunId { get; set; }
    /// <summary>供 UI 显示的状态：等待、入队、过期、跳过、错误等。</summary>
    public string Status { get; set; } = "等待";
    /// <summary>当前状态的具体原因。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>建立用于状态提交的独立副本。</summary>
    public PuloniaTaskTriggerState Clone() => new()
    {
        PlanId = PlanId,
        TriggerId = TriggerId,
        TriggerActivatedAtUtc = TriggerActivatedAtUtc,
        LastOccurrenceUtc = LastOccurrenceUtc,
        NextOccurrenceUtc = NextOccurrenceUtc,
        LastRunId = LastRunId,
        Status = Status,
        Message = Message
    };

    /// <summary>比较两个游标的全部持久化内容。</summary>
    public bool ContentEquals(PuloniaTaskTriggerState other)
        => PlanId == other.PlanId && TriggerId == other.TriggerId
           && TriggerActivatedAtUtc == other.TriggerActivatedAtUtc
           && LastOccurrenceUtc == other.LastOccurrenceUtc
           && NextOccurrenceUtc == other.NextOccurrenceUtc
           && LastRunId == other.LastRunId
           && Status == other.Status && Message == other.Message;
}
