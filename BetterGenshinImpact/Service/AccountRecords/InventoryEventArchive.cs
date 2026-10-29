using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>归档事件按日期独立保存，主库存文件只保留活动事件。</summary>
public sealed partial class InventoryStore
{
    /// <summary>使用结构化字段计算增量去重键，不受字段分隔符影响。</summary>
    private static string DeltaReceipt(string source, DateTimeOffset time) => "Delta:" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { Source = source, RecordedAt = time.UtcDateTime }))));

    /// <summary>新凭证显式保存 UTC 时间，便于确认库存覆盖后清理；来源仍使用哈希。</summary>
    private static string DeltaReceiptPrefix(string source, DateTimeOffset time) =>
        "DeltaV2:" + time.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))) + ":";

    /// <summary>增量内容摘要忽略到期批次顺序和等价时区表示。</summary>
    private static string DeltaContentKey(long quantity, List<InventoryExpiration>? expirations) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
        {
            Quantity = quantity,
            Expirations = expirations?.Select(item => item == null ? "null" : $"{item.Quantity}:{NormalizeReportTime(item.ExpiresAt)}")
                .OrderBy(value => value, StringComparer.Ordinal).ToArray()
        }))));

    /// <summary>被清理历史的稳定去重凭证。</summary>
    /// <remarks>未被确认库存覆盖的有效增量保留凭证；旧版无时间元数据的凭证保守保留。</remarks>
    private static bool HasReceipt(InventoryDocument data, string name, string receipt) =>
        data.EventReceipts.TryGetValue(name, out var receipts) && receipts.Contains(receipt);

    /// <summary>先可靠保存历史，再从主文件移出；中断后按事件 ID 去重。</summary>
    private void PersistInventory(InventoryDocument data)
    {
        if (!data.EventArchiveLoaded) LoadEventArchive(data);
        // 全部事件共用额度；只淘汰已归档或已完成的记录，保护尚未解决的状态。
        var removed = data.Events.Where(item => item.Archived || item.State == "Completed")
            .OrderBy(item => item.EndedAt ?? item.RecordedAt).ThenBy(item => item.Id)
            .Take(Math.Max(0, data.Events.Count - Settings().EventLimit)).ToArray();
        foreach (var item in removed)
        {
            if (!data.EventReceipts.TryGetValue(item.Name, out var receipts)) data.EventReceipts[item.Name] = receipts = [];
            receipts.Add("Id:" + item.Id);
            if (!item.ItemDeleted)
            {
                if (item.Kind == "Delta")
                {
                    // 有原事件可安全迁移旧凭证；错误事件不再占据已接收事件的去重键。
                    receipts.Remove(DeltaReceipt(item.Source, item.RecordedAt));
                    if (item.State != "Error") receipts.Add(DeltaReceiptPrefix(item.Source, item.RecordedAt) + DeltaContentKey(item.Quantity, item.Expirations));
                }
                if (item.ReportFingerprint.Length > 0) receipts.Add("Report:" + item.ReportFingerprint);
            }
        }
        var directory = Path.Combine(AccountDirectory(data.Uid), "events");
        foreach (var group in data.Events.Where(item => item.Archived).GroupBy(item =>
                     (item.EndedAt ?? item.RecordedAt).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
        {
            var path = Path.Combine(directory, group.Key + ".json");
            var previous = Read<List<InventoryEvent>>(path);
            var merged = previous.Concat(group).GroupBy(item => item.Id).Select(items => items.Last()).ToList();
            if (JsonConvert.SerializeObject(previous) != JsonConvert.SerializeObject(merged)) Write(path, merged);
        }
        var events = data.Events;
        try
        {
            data.Events = events.Where(item => !item.Archived && !HasReceipt(data, item.Name, "Id:" + item.Id)).ToList();
            Write(Path.Combine(AccountDirectory(data.Uid), "inventory.json"), data);
        }
        finally { data.Events = events; }
        // 凭证已写入主文件后才清理历史；中断重试不会重复接收已清理事件。
        data.Events = events.Where(item => !HasReceipt(data, item.Name, "Id:" + item.Id)).ToList();
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var previous = Read<List<InventoryEvent>>(path);
                var retained = previous.Where(item => !HasReceipt(data, item.Name, "Id:" + item.Id)).ToList();
                if (retained.Count == previous.Count) continue;
                if (retained.Count == 0) File.Delete(path);
                else Write(path, retained);
                File.Delete(path + ".bak");
            }
        // 所有归档清理成功后才移除恢复用 ID；任何中途异常都会保留已落盘的墓碑。
        var compacted = false;
        foreach (var pair in data.EventReceipts.ToArray())
        {
            var baseline = data.Items.GetValueOrDefault(pair.Key)?.FirstOrDefault();
            compacted |= pair.Value.RemoveWhere(value => value.StartsWith("Id:", StringComparison.Ordinal) ||
                baseline != null && value.StartsWith("DeltaV2:", StringComparison.Ordinal) &&
                long.TryParse(value.Split(':')[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) &&
                ticks < baseline.RecordedAt.UtcTicks) > 0;
            if (pair.Value.Count == 0) data.EventReceipts.Remove(pair.Key);
        }
        if (compacted)
        {
            var retainedEvents = data.Events;
            try
            {
                data.Events = retainedEvents.Where(item => !item.Archived).ToList();
                Write(Path.Combine(AccountDirectory(data.Uid), "inventory.json"), data);
            }
            finally { data.Events = retainedEvents; }
        }
    }

    /// <summary>历史用于追溯和跨归档去重，不因普通归档允许重复增量。</summary>
    /// <remarks>库存页面只读取活动事件；事件窗口和需要历史去重的写操作读取保留归档。</remarks>
    private void LoadEventArchive(InventoryDocument data, bool restoreBackup = true)
    {
        data.EventArchiveLoaded = true;
        var directory = Path.Combine(AccountDirectory(data.Uid), "events");
        if (!Directory.Exists(directory)) return;
        var events = new List<InventoryEvent>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var archived = Read<List<InventoryEvent>>(path, restoreBackup);
            if (archived.Any(item => item == null || item.History == null || !item.Archived))
                throw new InvalidDataException($"事件归档文件结构不正确，无法读取：{path}。");
            events.AddRange(archived.Where(item => !HasReceipt(data, item.Name, "Id:" + item.Id)));
        }
        data.Events = events.Concat(data.Events).GroupBy(item => item.Id).Select(items => items.Last()).OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
    }
}
