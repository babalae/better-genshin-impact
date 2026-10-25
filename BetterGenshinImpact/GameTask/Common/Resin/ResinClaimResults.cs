using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>统一领奖任务的正常结束与开始前不足结果，避免将未达目标误报为部分成功。</summary>
public static class ResinClaimResults
{
    /// <summary>构造执行结果；只在尚未开始且确认树脂不足时跳过，其余树脂结束返回未核验完成。</summary>
    /// <param name="selector">本次进度。</param>
    /// <param name="reason">结束原因。</param>
    /// <param name="ending">任务原有结束语，不含末尾标点。</param>
    /// <param name="hasStarted">是否已进入挑战，决定零领奖时是否仍应报告执行情况。</param>
    /// <param name="rewards">原奖励汇总，未识别时为空。</param>
    public static SoloTaskResult<ResinClaimData> Complete(ResinClaimSelector selector, ResinStopReason reason,
        string ending, bool hasStarted, RewardSummary? rewards = null)
    {
        if (!hasStarted && selector.ClaimCount == 0 && reason == ResinStopReason.ResinExhausted)
            return SoloTaskResult.Skipped(SoloTaskReasons.ResinExhausted, ResinUsageText.Skipped);
        var report = selector.BuildReport(reason);
        return SoloTaskResult.Unverified(new ResinClaimData { Rewards = rewards ?? new(), Resin = report },
            ending + "：" + ResinUsageText.Report(report));
    }
}
