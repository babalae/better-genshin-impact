using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>增量的到期批次校验与推算，确认库存只由完整上报或人工修订更新。</summary>
public sealed partial class InventoryStore
{
    /// <summary>插入迟到增量后检查每个历史时间点，禁止借用未来入库抵消此前的超额消耗。</summary>
    private static bool HasValidDeltaSequence(InventoryRecord baseline, InventoryEvent[] pending,
        InventoryDeltaReport incoming, DateTimeOffset recordedAt)
    {
        var quantity = (decimal)baseline.Quantity;
        // 与到期推算使用相同的稳定时间排序；同时间记录按接收顺序排列。
        foreach (var delta in pending.Select(item => (item.RecordedAt, Quantity: item.Quantity))
                     .Append((RecordedAt: recordedAt, Quantity: incoming.Delta)).OrderBy(item => item.RecordedAt))
        {
            quantity += delta.Quantity;
            if (quantity < 0 || quantity > long.MaxValue) return false;
        }
        return true;
    }
    /// <summary>按时间重放待核实增量，消耗优先使用最早到期批次，未注明到期的数量最后消耗。</summary>
    public static List<InventoryExpiration>? EstimateExpirations(InventoryRecord record, IEnumerable<InventoryEvent> events)
    {
        var pending = events.Where(item => item.Kind == "Delta" && item.State == "Pending" && !item.Archived)
            .OrderBy(item => item.RecordedAt).ToArray();
        if (record.Expirations == null && !pending.Any(item => item.Expirations != null)) return null;
        var batches = record.Expirations?.Select(item => new InventoryExpiration { Quantity = item.Quantity, ExpiresAt = item.ExpiresAt }).ToList() ?? [];
        foreach (var delta in pending)
        {
            if (delta.Quantity > 0 && delta.Expirations != null)
                batches.AddRange(delta.Expirations.Select(item => new InventoryExpiration { Quantity = item.Quantity, ExpiresAt = item.ExpiresAt }));
            if (delta.Quantity >= 0) continue;
            // 使用 decimal 避免 long.MinValue 取反溢出；剩余部分来自未注明到期的库存。
            var remaining = -(decimal)delta.Quantity;
            foreach (var batch in batches.OrderBy(item => item.Time))
            {
                var consumed = Math.Min(remaining, batch.Quantity);
                batch.Quantity -= (long)consumed;
                remaining -= consumed;
                if (remaining == 0) break;
            }
            batches.RemoveAll(item => item.Quantity == 0);
        }
        return batches.OrderBy(item => item.Time).ToList();
    }

    /// <summary>新增批次数量必须与增量一致；减少时由系统推算，不接收新增到期批次。</summary>
    private static string? ValidateDeltaExpirations(InventoryDeltaReport report, InventoryRecord baseline, InventoryEvent[] pending)
    {
        if (report.Delta <= 0)
            return report.Expirations is { Count: > 0 } ? "本次变动未增加库存，却附带了新增到期批次。请来源移除新增批次后重新上报；减少数量由系统按最早到期批次优先推算。" : null;
        var tracked = baseline.Expirations is { Count: > 0 } || pending.Any(item => item.Expirations is { Count: > 0 });
        if (report.Expirations is not { Count: > 0 })
            return tracked ? "该物品正在记录到期批次，本次增加缺少到期信息。请来源提供新增数量对应的到期时间后重新上报。" : null;
        var error = ValidateExpirations(report.Expirations, report.Delta);
        if (error != null) return error;
        return report.Expirations.Sum(item => (decimal)item.Quantity) != report.Delta
            ? "新增到期批次的数量合计与本次增加数量不一致。请来源核对两者后重新上报。" : null;
    }
}
