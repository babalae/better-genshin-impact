using BetterGenshinImpact.GameTask.Model.GameUI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 背包物品计数任务参数。
/// </summary>
public class CountInventoryItemParam
{
    public GridScreenName GridScreenName { get; set; }

    public string? ItemName { get; set; }

    public List<string> ItemNames { get; set; } = [];

    public ItemIconRecognitionMode IconRecognitionMode { get; set; } = ItemIconRecognitionMode.GridIcon;

    /// <summary>
    /// 供脚本创建后逐项赋值；参数校验在任务消费参数时执行。
    /// </summary>
    public CountInventoryItemParam()
    {
    }

    public IEnumerable<string>? GetItemNamesOrNull()
    {
        return ItemNames.Count > 0 ? ItemNames : null;
    }

    public void Validate()
    {
        ItemNames ??= [];
        bool hasItemName = !string.IsNullOrWhiteSpace(ItemName);
        bool hasItemNames = ItemNames.Count > 0;
        if (!hasItemName)
        {
            ItemName = null;
        }

        if (hasItemName && hasItemNames)
        {
            throw new ArgumentException($"参数{nameof(ItemName)}和{nameof(ItemNames)}不能同时使用");
        }

        if (!hasItemName && !hasItemNames)
        {
            throw new ArgumentException($"参数{nameof(ItemName)}和{nameof(ItemNames)}不能同时为空");
        }

        if (ItemNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"参数{nameof(ItemNames)}不能包含空名称");
        }
    }
}
