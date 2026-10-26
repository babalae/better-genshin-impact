using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划中一个阶段的执行记录：调用了哪个子任务、结果、树脂使用与本步奖励。
/// 未执行的阶段只记录跳过原因。
/// </summary>
public sealed class ResinPlanStep
{
    /// <summary>阶段。</summary>
    public ResinPlanStage Stage { get; set; }

    /// <summary>阶段的中文名称，供执行记录展示。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>调用的子任务标识。</summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>本步对象，例如秘境名、首领名或地脉花类型；没有时为空。</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>是否实际调用了子任务；为 false 时表示阶段在开始前跳过。</summary>
    public bool Executed { get; set; }

    /// <summary>结果种类。</summary>
    public SoloTaskResultKind Kind { get; set; } = SoloTaskResultKind.Skipped;

    /// <summary>机器可读的原因码。</summary>
    public string? ReasonCode { get; set; }

    /// <summary>结果或跳过原因的说明。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>子任务的树脂使用报告；未执行或子任务未开始领奖时为 null。</summary>
    public ResinUsageReport? Resin { get; set; }

    /// <summary>本步识别到的奖励。</summary>
    public RewardSummary Rewards { get; set; } = new();

    /// <summary>子任务是否具备奖励识别；为 false 时奖励列表为空不代表没有获得奖励。</summary>
    public bool RewardsRecognized { get; set; }

    /// <summary>合成阶段实际合成的浓缩份数；其他阶段为 null。</summary>
    public int? CraftedCount { get; set; }
}
