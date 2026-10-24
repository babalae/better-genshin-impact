using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 触发器最近一次调度处理的运行期反馈，只在服务内存中维护，不写入 state.json。
/// 重启后由运行记录推导最近一次请求；跳过、合并等没有产生运行的反馈不跨重启保留。
/// </summary>
public sealed record PuloniaTaskTriggerStatus
{
    /// <summary>所属计划 ID。</summary>
    public required string PlanId { get; init; }
    /// <summary>触发器 ID。</summary>
    public required string TriggerId { get; init; }
    /// <summary>反馈对应的触发配置版本；与当前配置不一致时界面不展示。</summary>
    public DateTimeOffset TriggerActivatedAtUtc { get; init; }
    /// <summary>最近一次处理结果的类别，逻辑判断只使用此枚举，不比较文案。</summary>
    public PuloniaTaskTriggerOutcome Outcome { get; init; }
    /// <summary>具体原因；关联运行时为该运行的当前消息。</summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>最近一次提交的运行 ID，未产生运行时为空。</summary>
    public string? RunId { get; init; }
    /// <summary>关联运行的当前状态，查询时从运行记录读取；未关联或运行不存在时为空。</summary>
    public PuloniaTaskRunStatus? RunStatus { get; init; }
    /// <summary>下一个日程时刻；热键入口或未来无有效时间时为空。</summary>
    public DateTimeOffset? NextOccurrenceUtc { get; init; }
}

/// <summary>触发器最近一次调度处理的结果类别。</summary>
public enum PuloniaTaskTriggerOutcome
{
    /// <summary>配置已生效，等待下一个日程时刻。</summary>
    Waiting,
    /// <summary>已提交运行请求，具体进度见关联运行状态。</summary>
    Submitted,
    /// <summary>发生时刻已遗漏（超出允许延迟启动时间或未启用补触发），未启动任务。</summary>
    Missed,
    /// <summary>同一计划/账号已有请求，按忙碌策略合并本次触发。</summary>
    Merged,
    /// <summary>快照准备或请求提交失败，本次发生时刻不再重试。</summary>
    PrepareFailed
}
