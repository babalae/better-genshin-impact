using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划的结果数据：本次消耗树脂获得的全部奖励、合计树脂使用、预算及各阶段记录。
/// </summary>
public sealed class ResinPlanData
{
    /// <summary>全部阶段识别到的奖励合并结果。</summary>
    public RewardSummary Rewards { get; set; } = new();

    /// <summary>全部阶段合计的树脂使用；领奖次数与各类消耗为各阶段之和。</summary>
    public ResinUsageReport Resin { get; set; } = new();

    /// <summary>预算与开始时的持有量。</summary>
    public ResinPlanBudgetReport Budget { get; set; } = new();

    /// <summary>准备阶段识别到的活动；未执行活动识别时为 null。</summary>
    public ResinPlanEventReport? Events { get; set; }

    /// <summary>按执行顺序排列的阶段记录。</summary>
    public List<ResinPlanStep> Steps { get; set; } = [];
}

/// <summary>
/// 体力计划的预算报告。
/// </summary>
public sealed class ResinPlanBudgetReport
{
    /// <summary>本次最多消耗的点数；0 表示全部。</summary>
    public int MaxResin { get; set; }

    /// <summary>开始时的原粹树脂。</summary>
    public int StartOriginal { get; set; }

    /// <summary>开始时的浓缩树脂；数量未识别时为 null。</summary>
    public int? StartCondensed { get; set; }

    /// <summary>本次领奖消耗的点数合计（原粹点数加每个浓缩 60 点）。</summary>
    public int SpentPoints { get; set; }

    /// <summary>本次合成的浓缩树脂份数。</summary>
    public int CraftedCondensed { get; set; }
}
