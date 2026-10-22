using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 背包物品计数任务的结果数据。
/// </summary>
public class InventoryCountData
{
    /// <summary>
    /// 找到的物品名称到数量的映射；未找到的物品不包含在内，识别到物品但数量无法识别时为 -2。
    /// </summary>
    public Dictionary<string, int> Counts { get; set; } = [];
}
