namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// 数量 OCR 对比任务的参数。
/// </summary>
public class InventoryCountComparisonParam
{
    /// <summary>
    /// 要打开并扫描的分类，或表示不导航的当前一页。
    /// </summary>
    public InventoryCountComparisonTarget Target { get; set; } = InventoryCountComparisonTarget.CharacterDevelopmentItems;
}
