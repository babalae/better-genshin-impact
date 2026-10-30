using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>统一物品当前提示的优先级及完成事件的批量归档资格。</summary>
public static class InventoryEventPolicy
{
    /// <summary>状态灯与底部卡片共用优先级。</summary>
    internal static int Priority(InventoryEvent item) => item.State == "Error" ? 0 :
        item.Kind == "ExpirationCheck" && item.State == "Pending" ? 1 :
        item.State == "Warning" ? 2 : item.State == "Pending" ? 3 : item.State == "Completed" ? 4 : 5;

    /// <summary>从未归档事件中选取当前提示，同优先级显示最新事件。</summary>
    public static InventoryEvent? SelectActive(IEnumerable<InventoryEvent> events) => events.Where(item => !item.Archived)
        .OrderBy(Priority)
        .ThenByDescending(item => item.Id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>仅选择当前显示同类绿灯且没有库存错误的物品；被其他提示遮挡的完成事件保留。</summary>
    public static InventoryEvent[] GetCompletedArchiveCandidates(InventoryDocument inventory, string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return [];
        return inventory.Events.Where(item => !item.Archived).GroupBy(item => item.Name, StringComparer.Ordinal)
            .Where(group => !inventory.Errors.ContainsKey(group.Key) && SelectActive(group) is { State: "Completed" } active && active.Kind == kind)
            .SelectMany(group => group.Where(item => item.State == "Completed" && item.Kind == kind)).ToArray();
    }
}
