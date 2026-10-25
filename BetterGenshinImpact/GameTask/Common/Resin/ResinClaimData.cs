using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>领奖任务的结果数据：奖励汇总与树脂使用报告。</summary>
public sealed class ResinClaimData
{
    /// <summary>识别到的奖励；未开启奖励识别时集合为空。</summary>
    public RewardSummary Rewards { get; set; } = new();

    /// <summary>本次树脂使用情况。</summary>
    public ResinUsageReport Resin { get; set; } = new();
}
