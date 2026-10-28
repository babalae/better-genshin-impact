using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 汇总体力计划的阶段记录并给出最终结果。纯计算，不操作游戏。
/// </summary>
public static class ResinPlanResults
{
    /// <summary>
    /// 合并各阶段的奖励与树脂使用，按阶段结果决定整次运行的结果种类：
    /// 有失败步骤时，已有领奖为部分完成、否则为失败；没有领奖为跳过；存在未核验步骤为未核验；其余为成功。
    /// </summary>
    /// <param name="data">本次运行的结果数据；奖励与合计树脂使用会被重写。</param>
    /// <returns>附带结果数据的任务结果。</returns>
    public static SoloTaskResult<ResinPlanData> Complete(ResinPlanData data)
    {
        // 1. 合并奖励与树脂使用
        var exhausted = IsExhausted(data);
        data.Rewards = MergeRewards(data.Steps);
        data.Resin = Aggregate(data.Steps, exhausted);
        var summary = "体力计划结束：" + ResinUsageText.Report(data.Resin);

        // 2. 按阶段结果决定整次运行的结果
        var failed = data.Steps.FirstOrDefault(step => step.Kind == SoloTaskResultKind.Failed);
        if (failed is not null)
        {
            var kind = data.Resin.ClaimCount > 0 ? SoloTaskResultKind.Partial : SoloTaskResultKind.Failed;
            return new SoloTaskResult<ResinPlanData>(kind, $"{summary}{failed.Name}失败：{failed.Message}",
                failed.ReasonCode ?? ResinPlanReasons.TaskException, data);
        }

        if (data.Resin.ClaimCount == 0)
        {
            return exhausted
                ? new SoloTaskResult<ResinPlanData>(SoloTaskResultKind.Skipped, ResinUsageText.Skipped, SoloTaskReasons.ResinExhausted, data)
                : new SoloTaskResult<ResinPlanData>(SoloTaskResultKind.Skipped, "体力计划结束：没有可领取的奖励。", ResinPlanReasons.NothingClaimed, data);
        }

        var unverified = data.Steps.Any(step => step.Executed
            && step.Kind is SoloTaskResultKind.Unverified or SoloTaskResultKind.Partial);
        return unverified ? SoloTaskResult.Unverified(data, summary) : SoloTaskResult.Success(data, summary);
    }

    /// <summary>准备阶段失败时的结果，仍附带已有的结果数据。</summary>
    /// <param name="data">结果数据。</param>
    /// <param name="reasonCode">原因码。</param>
    /// <param name="message">失败原因。</param>
    /// <returns>失败结果。</returns>
    public static SoloTaskResult<ResinPlanData> Failed(ResinPlanData data, string reasonCode, string message)
        => new(SoloTaskResultKind.Failed, message, reasonCode, data);

    /// <summary>
    /// 判断本次是否已经用完可用树脂：有阶段因树脂不足或达到用量上限而跳过，
    /// 或最后一个领奖阶段因树脂耗尽结束。
    /// </summary>
    /// <param name="data">结果数据。</param>
    /// <returns>可用树脂已用完时返回 true。</returns>
    internal static bool IsExhausted(ResinPlanData data)
        => data.Steps.Any(step => !step.Executed
               && step.ReasonCode is SoloTaskReasons.ResinExhausted or ResinPlanReasons.BudgetReached)
           || data.Steps.LastOrDefault(step => step.Resin is not null)?.Resin?.StopReason == ResinStopReason.ResinExhausted
           || ResinPlanBudget.Remaining(data.Budget.MaxResin, data.Budget.SpentPoints) is < ResinPlanBudget.MinClaimPoints;

    /// <summary>按名称合并各阶段识别到的奖励，保持首次出现的顺序。</summary>
    /// <param name="steps">阶段记录。</param>
    /// <returns>合并后的奖励。</returns>
    internal static RewardSummary MergeRewards(IEnumerable<ResinPlanStep> steps)
    {
        var merged = new Dictionary<string, int>();
        foreach (var item in steps.SelectMany(step => step.Rewards.Items))
        {
            merged[item.Name] = merged.GetValueOrDefault(item.Name) + item.Count;
        }

        return RewardSummary.From(merged);
    }

    /// <summary>合计各阶段的领奖次数与消耗，作为整次运行的树脂使用报告。</summary>
    /// <param name="steps">阶段记录。</param>
    /// <param name="exhausted">可用树脂是否已用完，决定报告的结束说明。</param>
    /// <returns>合计报告。</returns>
    internal static ResinUsageReport Aggregate(IEnumerable<ResinPlanStep> steps, bool exhausted)
    {
        var report = new ResinUsageReport
        {
            Mode = ResinUsageMode.Exhaust,
            StopReason = exhausted ? ResinStopReason.ResinExhausted : ResinStopReason.TargetReached
        };
        foreach (var resin in steps.Select(step => step.Resin).OfType<ResinUsageReport>())
        {
            report.ClaimCount += resin.ClaimCount;
            foreach (var kind in ResinKinds.InOrder)
            {
                report.Claims[kind] += resin.Claims[kind];
                report.Consumed[kind] += resin.Consumed[kind];
            }
        }

        return report;
    }
}
