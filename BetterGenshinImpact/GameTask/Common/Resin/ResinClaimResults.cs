using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>统一领奖任务的正常结束与开始前不足结果，避免将未达目标误报为部分成功。</summary>
public static class ResinClaimResults
{
    /// <summary>
    /// 构造执行结果：尚未开始且确认树脂不足时跳过；按树脂设置刷取完成或材料目标达成返回成功；
    /// 其余提前结束（树脂不足、未找到可用选项）返回未核验完成。
    /// </summary>
    /// <param name="selector">本次进度。</param>
    /// <param name="reason">结束原因。</param>
    /// <param name="ending">任务原有结束语，不含末尾标点。</param>
    /// <param name="hasStarted">是否已进入挑战，决定零领奖时是否仍应报告执行情况。</param>
    /// <param name="rewards">原奖励汇总，未识别时为空。</param>
    /// <param name="targetItemsReached">材料目标是否已由奖励识别或背包增量确认全部达成。</param>
    public static SoloTaskResult<ResinClaimData> Complete(ResinClaimSelector selector, ResinStopReason reason,
        string ending, bool hasStarted, RewardSummary? rewards = null, bool targetItemsReached = false)
    {
        if (!hasStarted && selector.ClaimCount == 0 && reason == ResinStopReason.ResinExhausted)
            return SoloTaskResult.Skipped(SoloTaskReasons.ResinExhausted, ResinUsageText.Skipped);
        var report = selector.BuildReport(reason);
        var data = new ResinClaimData { Rewards = rewards ?? new(), Resin = report };
        var message = ending + "：" + ResinUsageText.Report(report);

        // 材料目标基于识别到的实际奖励判定，有到账证据，按成功返回。
        if (targetItemsReached)
            return SoloTaskResult.Success(data, message + "材料目标已全部达成。");
        // 领奖只在使用按钮点击成功后记账，按树脂设置刷取完成即视为成功；未达设置即结束仍无法确认完成情况。
        return IsResinUsageCompleted(report)
            ? SoloTaskResult.Success(data, message)
            : SoloTaskResult.Unverified(data, message);
    }

    /// <summary>判断是否已按树脂设置刷取完成：达到指定目标，或刷至耗尽方式至少领奖一次后树脂用完。</summary>
    /// <param name="report">本次树脂使用报告。</param>
    /// <returns>按设置完成时返回 true；提前结束或未找到可用选项时返回 false。</returns>
    private static bool IsResinUsageCompleted(ResinUsageReport report)
        => report.StopReason == ResinStopReason.TargetReached
           || (report.Mode == ResinUsageMode.Exhaust && report.StopReason == ResinStopReason.ResinExhausted
               && report.ClaimCount > 0);
}
