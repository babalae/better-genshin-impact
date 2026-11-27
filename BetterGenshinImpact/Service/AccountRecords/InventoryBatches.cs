using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>库存批次历史；沿用库存互斥锁，原始输入与当前处理状态分开保存。</summary>
public sealed partial class InventoryStore
{
    /// <summary>限制外部标识长度；路径使用摘要，大小写不同的标识不会在 Windows 上碰撞。</summary>
    private static string BatchIdentifier(string? value) => value == null ? Guid.NewGuid().ToString("N") :
        value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? value : throw new ArgumentException("批次和记录标识须为 1～128 个 ASCII 字母、数字、连字符或下划线。");

    /// <summary>稳定摘要，用于文件名和内容身份校验。</summary>
    private static string BatchHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>观测内容去重：记录身份不纳入业务内容摘要。</summary>
    private static string ObservationJson(object input)
    {
        var token = JToken.FromObject(input);
        foreach (var item in token is JArray array ? array.OfType<JObject>() : new[] { (JObject)token }) item.Remove("RecordId");
        return token.ToString(Formatting.None);
    }

    /// <summary>创建输入证据；时间文本保持原样，不以接收时间替代。</summary>
    private static InventoryBatchRecord BatchInput(string id, string name, string source, string kind, object input)
    {
        var payload = JObject.FromObject(input);
        // ID 已在信封中，不纳入业务内容；传输时是否显式提供自动编号不改变摘要。
        payload.Remove("RecordId");
        return new InventoryBatchRecord { RecordId = BatchIdentifier(id), Name = name.Trim(), Source = source,
            Kind = kind, Input = payload, ContentHash = BatchHash(JsonConvert.SerializeObject(new { Name = name.Trim(), Source = source, Kind = kind, Input = payload })) };
    }

    /// <summary>预检整批身份冲突，在任何业务变更前执行；失败记录允许相同内容重新校验。</summary>
    private InventoryBatch BeginBatch(InventoryDocument data, string? batchId, string kind, IEnumerable<InventoryBatchRecord> records)
    {
        var batch = new InventoryBatch { BatchId = BatchIdentifier(batchId), Kind = kind,
            ReceivedAt = DateTimeOffset.UtcNow, Records = records.ToList() };
        if (batch.Records.Select(item => item.RecordId).Distinct(StringComparer.Ordinal).Count() != batch.Records.Count)
            throw new ArgumentException("同一批次中的 RecordId 必须唯一，本批上报未保存。");
        batch.RecordIndex = batch.Records.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
        var receipts = data.BatchReceipts.GetValueOrDefault(batch.BatchId);
        // 恢复的库存可能缺少已发布记录的回执，先核对历史身份，避免提交后才发现冲突。
        Dictionary<string, InventoryBatchRecord>? published = null;
        if (batch.Records.Any(record => receipts?.ContainsKey(record.RecordId) != true))
            published = ReadBatch(data.Uid, batch.BatchId).Records.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
        foreach (var record in batch.Records)
            if ((receipts?.GetValueOrDefault(record.RecordId)?.ContentHash ?? published?.GetValueOrDefault(record.RecordId)?.ContentHash) is { } hash && hash != record.ContentHash)
                throw new InvalidOperationException($"记录 {batch.BatchId} / {record.RecordId} 已存在但内容不同。本批未接收，请为新的输入使用新记录标识。");
        batch.OriginalCount = batch.Records.Count;
        return batch;
    }

    /// <summary>为当前输入选择引用；返回是否已经成功接收，不绕过失败重试规则。</summary>
    private static bool SelectBatchRecord(InventoryDocument data, InventoryBatch batch, string recordId)
    {
        data.ProcessingReference = new() { BatchId = batch.BatchId, RecordId = recordId };
        var accepted = data.BatchReceipts.GetValueOrDefault(batch.BatchId)?.GetValueOrDefault(recordId)?.Accepted == true;
        batch.RecordIndex[recordId].AlreadyAccepted = accepted;
        return accepted;
    }

    /// <summary>记录逐条结果；组内重复物品仍各自保留输入，不改变现有按名称校验规则。</summary>
    private static void CompleteBatchRecords(InventoryBatch batch, IEnumerable<string> ids, string outcome, string message = "")
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var id in selected)
        {
            var record = batch.RecordIndex[id];
            // 组内新输入被拒绝时，已成功接收的旧输入仍按重复提交返回。
            var acceptedReplay = record.AlreadyAccepted && outcome == "Rejected";
            record.Outcome = acceptedReplay ? "Duplicate" : outcome;
            record.Message = acceptedReplay ? "同一批次记录已处理。" : message;
            record.ProcessedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>把待发布历史和去重凭证加入当前状态，随后由同一次库存保存提交。</summary>
    private static void QueueBatch(InventoryDocument data, InventoryBatch batch, InventoryReportResult? result = null)
    {
        if (batch.Records.Any(item => item.Outcome.Length == 0)) throw new InvalidOperationException("批次存在尚未处理的记录，不能保存。");
        if (batch.Records.Count > 0)
        {
            if (!data.BatchReceipts.TryGetValue(batch.BatchId, out var receipts)) data.BatchReceipts[batch.BatchId] = receipts = new(StringComparer.Ordinal);
            foreach (var record in batch.Records)
                receipts[record.RecordId] = new() { Name = record.Name, ContentHash = record.ContentHash,
                    Accepted = receipts.GetValueOrDefault(record.RecordId)?.Accepted == true || record.Outcome is "Accepted" or "Duplicate" };
            var changed = batch.Records.Where(item => !item.AlreadyAccepted).ToList();
            if (changed.Count > 0) data.PendingBatches.Add(new InventoryBatch { BatchId = batch.BatchId, Kind = batch.Kind,
                ReceivedAt = batch.ReceivedAt, OriginalCount = changed.Count, Records = changed });
        }
        data.ProcessingReference = null;
        if (result != null) { result.BatchId = batch.BatchId; result.Records.AddRange(batch.Records); }
    }

    /// <summary>批次按账号保存，文件不包含 UID，账号改名时直接随目录迁移。</summary>
    private string BatchPath(string uid, string batchId) => Path.Combine(AccountDirectory(uid), "batches", BatchHash(batchId) + ".json");

    /// <summary>读取并校验批次身份，损坏的历史不作为空批次覆盖。</summary>
    private InventoryBatch ReadBatch(string uid, string batchId, bool restoreBackup = true)
    {
        var path = BatchPath(uid, batchId);
        if (!File.Exists(path)) return new InventoryBatch { BatchId = batchId };
        InventoryBatch batch;
        var recovered = false;
        try
        {
            batch = JsonConvert.DeserializeObject<InventoryBatch>(File.ReadAllText(path),
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })
                ?? throw new JsonSerializationException("批次文件为空。");
        }
        catch (JsonException)
        {
            batch = Read<InventoryBatch>(path, restoreBackup: false);
            recovered = true;
        }
        if (batch.SchemaVersion != 1 || batch.BatchId != batchId || batch.Records == null ||
            batch.Records.Any(item => item == null || item.Input == null || string.IsNullOrEmpty(item.RecordId)) ||
            batch.Records.Select(item => item.RecordId).Distinct(StringComparer.Ordinal).Count() != batch.Records.Count)
            throw new InvalidDataException("批次历史结构或身份不正确：" + path);
        if (recovered)
        {
            // 先纳入库存中已提交的待发布结果，再核对接受凭证；旧备份不能推翻最新状态。
            var committed = Load(uid, includeHistory: false, restoreBackup: false);
            var receipts = committed.BatchReceipts.GetValueOrDefault(batchId);
            var records = batch.Records.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
            foreach (var pending in committed.PendingBatches.Where(item => item.BatchId == batchId))
            {
                foreach (var record in pending.Records)
                {
                    if (receipts?.TryGetValue(record.RecordId, out var receipt) != true ||
                        receipt.ContentHash != record.ContentHash ||
                        (records.TryGetValue(record.RecordId, out var previous) && previous.ContentHash != record.ContentHash))
                        throw new InvalidDataException("待发布记录与批次历史内容冲突，已保留恢复依据。");
                    records[record.RecordId] = record;
                }
            }
            if (receipts != null && receipts.Any(entry => entry.Value.Accepted &&
                (!records.TryGetValue(entry.Key, out var record) || record.ContentHash != entry.Value.ContentHash ||
                 record.Outcome is not ("Accepted" or "Duplicate"))))
                throw new InvalidDataException("批次历史不完整：备份与已提交的接受凭证不一致。");
            batch.Records = records.Values.ToList();
            batch.OriginalCount = Math.Max(batch.OriginalCount, receipts?.Count ?? 0);
            batch.RemovedCount = batch.OriginalCount - batch.Records.Count;
            if (restoreBackup)
            {
                File.Copy(path, path + ".corrupt", true);
                Write(path, batch, retainPrevious: false);
            }
        }
        return batch;
    }

    /// <summary>只保存当前状态；调用时待发布批次必须仍在内存中，异常不能撤销已提交库存。</summary>
    private void WriteCurrentInventory(InventoryDocument data)
    {
        data.SchemaVersion = 3;
        var events = data.Events;
        try
        {
            data.Events = events.Where(item => !item.Archived).ToList();
            // 清除发布标记不算新的业务提交，保留上一版业务状态备份。
            Write(Path.Combine(AccountDirectory(data.Uid), "inventory.json"), data, retainPrevious: false);
        }
        finally { data.Events = events; }
    }

    /// <summary>幂等发布已提交的批次；批次和索引全部落盘后才清除待发布内容。</summary>
    private void PublishBatches(InventoryDocument data)
    {
        if (data.PendingBatches.Count == 0) return;
        // 按组合身份一次建立索引，批量发布无需为每条输入扫描全部历史。
        var index = new Dictionary<(string BatchId, string RecordId), InventoryBatchIndexEntry>();
        foreach (var entry in BatchIndex(data.Uid))
            index[(entry.Reference.BatchId, entry.Reference.RecordId)] = entry;
        // 必须先使旧索引失效，避免写完部分批次就中断时仍查询到过期索引。
        InvalidateBatchIndex(data.Uid);
        foreach (var pending in data.PendingBatches)
        {
            var batch = ReadBatch(data.Uid, pending.BatchId);
            if (batch.Kind.Length == 0) { batch.Kind = pending.Kind; batch.ReceivedAt = pending.ReceivedAt; }
            // 导入本身允许混合记录；其他批次追加不同操作时明确标识为 Mixed。
            else if (batch.Kind != pending.Kind && !(batch.Kind == "Import" && pending.Kind is "Report" or "Delta"))
                batch.Kind = "Mixed";
            // 每批仅建立一次位置索引，替换时保留历史顺序，新增记录追加到末尾。
            var positions = batch.Records.Select((record, position) => (record.RecordId, position))
                .ToDictionary(item => item.RecordId, item => item.position, StringComparer.Ordinal);
            foreach (var record in pending.Records)
            {
                if (positions.TryGetValue(record.RecordId, out var previous))
                {
                    if (batch.Records[previous].ContentHash != record.ContentHash)
                        throw new InvalidDataException("待发布记录与批次历史内容冲突，已保留恢复依据。");
                    batch.Records[previous] = record;
                }
                else { positions.Add(record.RecordId, batch.Records.Count); batch.Records.Add(record); batch.OriginalCount++; }
                index[(batch.BatchId, record.RecordId)] = new() { Name = record.Name, ProcessedAt = record.ProcessedAt,
                    Reference = new() { BatchId = batch.BatchId, RecordId = record.RecordId } };
            }
            batch.OriginalCount = Math.Max(batch.OriginalCount, data.BatchReceipts.GetValueOrDefault(batch.BatchId)?.Count ?? 0);
            batch.RemovedCount = batch.OriginalCount - batch.Records.Count;
            Write(BatchPath(data.Uid, batch.BatchId), batch);
        }
        Write(Path.Combine(AccountDirectory(data.Uid), "batch-index.json"), index.Values.ToList());
        data.PendingBatches.Clear();
        WriteCurrentInventory(data);
    }

    /// <summary>先重放已提交的归档状态，再清除恢复依据；只合并涉及的日期。</summary>
    private void PublishEventArchives(InventoryDocument data)
    {
        if (data.PendingEventArchives.Count == 0) return;
        foreach (var group in data.PendingEventArchives.GroupBy(ArchiveDate))
        {
            var path = Path.Combine(AccountDirectory(data.Uid), "events", group.Key + ".json");
            var previous = Read<List<InventoryEvent>>(path);
            var merged = previous.Concat(group).GroupBy(item => item.Id).Select(items => items.Last()).ToList();
            if (JsonConvert.SerializeObject(previous) != JsonConvert.SerializeObject(merged)) Write(path, merged);
        }
        data.PendingEventArchives.Clear();
        WriteCurrentInventory(data);
    }

    /// <summary>批次变更使索引失效，不依赖易丢失的内存缓存。</summary>
    private void InvalidateBatchIndex(string uid)
    {
        var path = Path.Combine(AccountDirectory(uid), "batch-index.json");
        File.Delete(path);
        File.Delete(path + ".bak");
    }

    /// <summary>缺失索引时从批次证据重建；日常按物品查询只读索引和命中的批次。</summary>
    private List<InventoryBatchIndexEntry> BatchIndex(string uid, bool readOnly = false)
    {
        var path = Path.Combine(AccountDirectory(uid), "batch-index.json");
        if (File.Exists(path))
        {
            try
            {
                var cached = JsonConvert.DeserializeObject<List<InventoryBatchIndexEntry>>(File.ReadAllText(path));
                if (cached == null || cached.Any(item => item == null || item.Reference == null || item.Name == null ||
                    !IsBatchIdentifier(item.Reference.BatchId) || !IsBatchIdentifier(item.Reference.RecordId)))
                    throw new InvalidDataException("批次索引结构无效，将从历史重建。");
                return cached;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { if (!readOnly) InvalidateBatchIndex(uid); }
            catch (IOException) when (readOnly)
            {
                // 索引被独占时仍可只读扫描批次；写入路径保留错误及发布重试。
            }
        }
        var result = new List<InventoryBatchIndexEntry>();
        var directory = Path.Combine(AccountDirectory(uid), "batches");
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var batch = Read<InventoryBatch>(file, restoreBackup: false);
                    if (!IsBatchIdentifier(batch.BatchId)) throw new InvalidDataException("批次标识无效。");
                    batch = ReadBatch(uid, batch.BatchId, restoreBackup: !readOnly);
                    result.AddRange(batch.Records.Select(record => new InventoryBatchIndexEntry { Name = record.Name,
                        ProcessedAt = record.ProcessedAt, Reference = new() { BatchId = batch.BatchId, RecordId = record.RecordId } }));
                }
                catch (Exception ex) when (readOnly && ex is IOException or InvalidDataException or JsonException or ArgumentException)
                {
                    // 索引缺失时无法为损坏文件定位记录；有效批次仍可查询。
                    Serilog.Log.Warning(ex, "狛荷屋历史索引重建跳过不可读批次 {File}", file);
                }
            }
        if (!readOnly) Write(path, result);
        return result;
    }

    /// <summary>一次取得物品的完整索引快照，供详情固定翻页范围，避免重试改变排序后漏记录。</summary>
    public Task<InventoryBatchIndexEntry[]> GetItemHistorySnapshotAsync(string uid, string name) => Locked(() =>
    {
        var data = Load(uid, includeHistory: false, restoreBackup: false);
        // 查询合并已提交但尚未发布的记录，不要求索引或批次文件当前可写。
        var pending = data.PendingBatches.SelectMany(batch => batch.Records.Select(record => new InventoryBatchIndexEntry
        { Name = record.Name, ProcessedAt = record.ProcessedAt, Reference = new() { BatchId = batch.BatchId, RecordId = record.RecordId } }));
        return BatchIndex(uid, readOnly: true).Concat(pending)
            .GroupBy(item => (item.Reference.BatchId, item.Reference.RecordId)).Select(group => group.Last()).Where(item => item.Name == name).OrderByDescending(item => item.ProcessedAt)
            .ThenBy(item => item.Reference.BatchId, StringComparer.Ordinal).ThenBy(item => item.Reference.RecordId, StringComparer.Ordinal).ToArray();
    });

    /// <summary>按固定快照引用读取一页证据；结果与引用顺序一致，被清理的记录返回 null，同批只读一次。</summary>
    public Task<InventoryBatchRecord?[]> GetItemHistoryPageAsync(string uid, IReadOnlyList<InventoryRecordReference> references)
    {
        if (references.Count > 1000) throw new ArgumentException("每页最多读取 1000 条历史记录。", nameof(references));
        var page = references.ToArray();
        return Locked(() =>
        {
            var data = Load(uid, includeHistory: false, restoreBackup: false);
            var batches = new Dictionary<string, Dictionary<string, InventoryBatchRecord>>(StringComparer.Ordinal);
            foreach (var id in page.Select(item => item.BatchId).Distinct(StringComparer.Ordinal))
            {
                InventoryBatch? batch = null;
                try { batch = ReadHistoryBatch(data, BatchIdentifier(id)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) { }
                // 一批不可读不能阻断整页；null 由界面明确计入无法读取的条数。
                batches[id] = batch?.Records.ToDictionary(item => item.RecordId, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            }
            return page.Select(item => batches[item.BatchId].GetValueOrDefault(item.RecordId)).ToArray();
        });
    }

    /// <summary>读取一批的保留记录及清理计数；不存在时返回 null。</summary>
    public Task<InventoryBatch?> GetBatchAsync(string uid, string batchId) => Locked<InventoryBatch?>(() =>
    {
        _ = BatchIdentifier(batchId);
        var data = Load(uid, includeHistory: false, restoreBackup: false);
        return ReadHistoryBatch(data, batchId);
    });

    /// <summary>验证持久索引中的标识，缺失或非法时重新建立索引。</summary>
    private static bool IsBatchIdentifier(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>只读合并已发布历史与已提交待发布结果，后者代表最新提交。</summary>
    private InventoryBatch? ReadHistoryBatch(InventoryDocument data, string batchId)
    {
        var pending = data.PendingBatches.Where(item => item.BatchId == batchId).ToArray();
        InventoryBatch batch;
        try { batch = ReadBatch(data.Uid, batchId, restoreBackup: false); }
        catch (Exception ex) when (pending.Length > 0 && ex is IOException or InvalidDataException or JsonException)
        {
            // 仍可展示已提交原始输入；其余历史数量由凭证给出，不伪造旧结果。
            batch = new InventoryBatch { BatchId = batchId };
        }
        if (batch.Records.Count == 0 && pending.Length == 0 && !File.Exists(BatchPath(data.Uid, batchId))) return null;
        var records = batch.Records.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
        foreach (var update in pending)
        {
            if (batch.Kind.Length == 0) { batch.Kind = update.Kind; batch.ReceivedAt = update.ReceivedAt; }
            else if (batch.Kind != update.Kind && !(batch.Kind == "Import" && update.Kind is "Report" or "Delta")) batch.Kind = "Mixed";
            foreach (var record in update.Records)
            {
                if (records.TryGetValue(record.RecordId, out var previous) && previous.ContentHash != record.ContentHash)
                    throw new InvalidDataException("待发布记录与批次历史内容冲突。");
                records[record.RecordId] = record;
            }
        }
        batch.Records = records.Values.ToList();
        batch.OriginalCount = Math.Max(Math.Max(batch.OriginalCount, batch.Records.Count), data.BatchReceipts.GetValueOrDefault(batchId)?.Count ?? 0);
        batch.RemovedCount = batch.OriginalCount - batch.Records.Count;
        return batch;
    }

}
