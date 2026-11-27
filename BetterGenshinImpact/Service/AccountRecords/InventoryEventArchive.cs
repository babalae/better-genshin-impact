using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>归档事件按日期独立保存，主库存文件只保留活动事件。</summary>
public sealed partial class InventoryStore
{
    /// <summary>事件所属的 UTC 归档日期。</summary>
    private static string ArchiveDate(InventoryEvent item) =>
        (item.EndedAt ?? item.RecordedAt).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>先提交当前状态及待发布历史，再写独立归档；中断后按事件 ID 幂等恢复。</summary>
    private void PersistInventory(InventoryDocument data)
    {
        if (!data.EventArchiveLoaded) LoadEventArchive(data);
        data.SchemaVersion = 3;
        var directory = Path.Combine(AccountDirectory(data.Uid), "events");
        var changed = data.Events.Where(item => item.Archived && item.ArchiveDirty).ToArray();
        foreach (var item in changed.Where(item => item.ArchiveFileDate != null && item.ArchiveFileDate != ArchiveDate(item)))
            data.PendingArchiveCleanupDates.Add(item.ArchiveFileDate!);
        // 当前状态、去重凭证、待发布归档和批次先在同一个文件内提交。
        // 历史写入失败时，下次加载重放，不能因跨文件中断丢失核实或归档结果。
        data.PendingEventArchives = changed.ToList();
        var events = data.Events;
        try
        {
            data.Events = events.Where(item => !item.Archived).ToList();
            Write(Path.Combine(AccountDirectory(data.Uid), "inventory.json"), data);
        }
        finally { data.Events = events; }
        PublishEventArchives(data);
        PublishBatches(data);
        // 归档日期变化时，先发布新位置，再清理旧位置。
        var locations = data.Events.Where(item => item.Archived).ToDictionary(item => item.Id, ArchiveDate);
        foreach (var date in data.PendingArchiveCleanupDates)
        {
            var path = Path.Combine(directory, date + ".json");
            var previous = Read<List<InventoryEvent>>(path);
            var retained = previous.Where(item => !locations.TryGetValue(item.Id, out var currentDate) || currentDate == date).ToList();
            if (retained.Count != previous.Count)
            {
                if (retained.Count == 0) File.Delete(path);
                else Write(path, retained);
            }
            // 即使主文件清理已完成，上次中断留下的备份也必须清理后才能清除待清理标记。
            File.Delete(path + ".bak");
        }
        // 所有旧日期清理成功后才移除恢复标记；中途异常保留待清理日期。
        var compacted = data.PendingArchiveCleanupDates.Count > 0;
        data.PendingArchiveCleanupDates.Clear();
        if (compacted)
        {
            // 清理恢复凭证不构成新的业务版本，不覆盖上一版库存备份。
            WriteCurrentInventory(data);
        }
        foreach (var item in data.Events.Where(item => item.Archived))
        {
            item.ArchiveDirty = false;
            item.ArchiveFileDate = ArchiveDate(item);
        }
    }

    /// <summary>历史用于追溯和跨归档去重，不因普通归档允许重复增量。</summary>
    /// <remarks>库存页面只读取活动事件；事件窗口和需要历史去重的写操作读取保留归档。</remarks>
    private void LoadEventArchive(InventoryDocument data, bool restoreBackup = true)
    {
        data.EventArchiveLoaded = true;
        var directory = Path.Combine(AccountDirectory(data.Uid), "events");
        var events = new List<InventoryEvent>();
        var files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json") : Enumerable.Empty<string>();
        foreach (var path in files.OrderBy(path => path, StringComparer.Ordinal))
        {
            var archived = Read<List<InventoryEvent>>(path, restoreBackup);
            if (archived.Any(item => item == null || item.History == null || !item.Archived))
                throw new InvalidDataException($"事件归档文件结构不正确，无法读取：{path}。");
            var date = Path.GetFileNameWithoutExtension(path);
            foreach (var item in archived)
            {
                item.ArchiveDirty = false;
                item.ArchiveFileDate = date;
            }
            events.AddRange(archived);
        }
        // 待发布内容已随主库存提交；只读查询也应看到最新状态，但不触发磁盘恢复。
        data.Events = events.Concat(data.Events).Concat(data.PendingEventArchives).GroupBy(item => item.Id).Select(items => items.Last()).OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
    }
}
