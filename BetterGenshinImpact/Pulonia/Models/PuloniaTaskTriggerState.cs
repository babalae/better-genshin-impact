using System;

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
    /// <summary>配置签名，编辑后重建待触发时间，不沿用旧等待条件。</summary>
    public string Signature { get; set; } = string.Empty;
    /// <summary>已经处理的最晚日程时刻；时钟回拨不得重放。</summary>
    public DateTimeOffset? LastOccurrenceUtc { get; set; }
    /// <summary>下一个日程时刻，空值表示一次性日程已结束或热键入口。</summary>
    public DateTimeOffset? NextOccurrenceUtc { get; set; }
    /// <summary>已观察到、正在等待提交的发生时刻。</summary>
    public DateTimeOffset? PendingOccurrenceUtc { get; set; }
    /// <summary>最近一次入队的请求。</summary>
    public Guid? LastRequestId { get; set; }
    /// <summary>供 UI 显示的状态：等待、入队、过期、跳过、错误等。</summary>
    public string Status { get; set; } = "等待";
    /// <summary>当前状态的具体原因。</summary>
    public string Message { get; set; } = string.Empty;
}
