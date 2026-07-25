namespace BetterGenshinImpact.GameTask.Common.Reward;

/// <summary>
/// 奖励物品
/// </summary>
public class RewardItem
{
    /// <summary>
    /// 物品名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 稀有度等级（-1 表示未知或不支持）
    /// </summary>
    public int QualityLevel { get; set; } = -1;

    /// <summary>
    /// 数量（-1表示未识别到数量）
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 卡片在识别时的位置索引（用于调试和去重）
    /// </summary>
    public int PositionIndex { get; set; }

    public RewardItem()
    {
    }

    public RewardItem(string name, int qualityLevel, int quantity, int positionIndex = -1)
    {
        Name = name;
        QualityLevel = qualityLevel;
        Quantity = quantity;
        PositionIndex = positionIndex;
    }

    public override string ToString()
    {
        return $"{Name} x{Quantity}";
    }
}
