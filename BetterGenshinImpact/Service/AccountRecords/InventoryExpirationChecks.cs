using System;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>过期库存的内置核实提醒，不依赖用户阈值规则，也不自动扣减库存。</summary>
public sealed partial class InventoryStore
{
    /// <summary>每种物品最多保留一条待检查事件；缺失到期信息不视为已解决。</summary>
    private static bool RefreshExpirationChecks(InventoryDocument data)
    {
        var changed = false;
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in data.Items)
        {
            var record = pair.Value.FirstOrDefault();
            if (record == null) continue;
            if (record.Quantity > 0 && record.Expirations == null) continue;
            var pending = data.Events.FirstOrDefault(item => item.Name == pair.Key && item.Kind == "ExpirationCheck" && !item.Archived);
            var expired = record.Quantity > 0 && record.Expirations?.Any(batch => batch.Time <= now) == true;
            if (expired && pending == null)
            {
                AddEvent(data, pair.Key, record.Source, "ExpirationCheck", "Pending", record.Quantity, now,
                    "记录中存在已到期批次，尚未自动扣减库存。请重新采集并上报到期信息，或查看游戏后手动填写当前实际库存总数。");
                changed = true;
            }
            else if (!expired && pending != null)
            {
                Transition(pending, "Completed", "库存已更新，已过期批次的检查已完成。", true);
                changed = true;
            }
        }
        return changed;
    }
}
