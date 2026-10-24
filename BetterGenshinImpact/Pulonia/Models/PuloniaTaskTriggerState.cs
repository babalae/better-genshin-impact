using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// state.json 中的定时触发游标，与排队请求在同一次状态替换提交。
/// 只保存防重复与补触发必需的事实；最近结果、下次时间等运行反馈由服务在内存中维护（见 <see cref="PuloniaTaskTriggerStatus"/>）。
/// 热键入口不写游标。
/// </summary>
public sealed class PuloniaTaskTriggerState
{
    /// <summary>所属计划 ID。</summary>
    public string PlanId { get; set; } = string.Empty;
    /// <summary>触发器 ID。</summary>
    public string TriggerId { get; set; } = string.Empty;
    /// <summary>触发配置的生效时间；编辑后重建待触发时间，不沿用旧等待条件。</summary>
    public DateTimeOffset TriggerActivatedAtUtc { get; set; }
    /// <summary>已经处理的最晚日程时刻；重启后从这里继续，时钟回拨不得重放。</summary>
    public DateTimeOffset? LastOccurrenceUtc { get; set; }

    /// <summary>建立用于状态提交的独立副本。</summary>
    public PuloniaTaskTriggerState Clone() => new()
    {
        PlanId = PlanId,
        TriggerId = TriggerId,
        TriggerActivatedAtUtc = TriggerActivatedAtUtc,
        LastOccurrenceUtc = LastOccurrenceUtc
    };
}
