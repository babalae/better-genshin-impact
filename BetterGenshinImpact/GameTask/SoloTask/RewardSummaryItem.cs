namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 奖励汇总中的一项。
/// </summary>
public sealed class RewardSummaryItem
{
    /// <summary>
    /// 物品名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 本次观测到的数量。
    /// </summary>
    public int Count { get; set; }
}
