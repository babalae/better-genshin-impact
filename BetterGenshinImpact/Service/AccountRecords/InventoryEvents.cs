using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>增量、核实和事件归档；所有操作通过同一存储锁串行化。</summary>
public sealed partial class InventoryStore
{
    /// <summary>原子归档当前账号的全部已完成事件，保留证据和其他状态。</summary>
    public Task<int> ArchiveCompletedEventsAsync(string uid) => Locked(() =>
    {
        var data = Load(uid);
        var completed = data.Events.Where(item => item.State == "Completed" && !item.Archived).ToArray();
        foreach (var item in completed) Transition(item, item.State, "用户已批量归档。", true);
        if (completed.Length > 0) PersistInventory(data);
        return completed.Length;
    });
    /// <summary>取得尚未核实的增量；绿灯保留并不代表增量仍待核实。</summary>
    private static InventoryEvent[] Pending(InventoryDocument data, string name) => data.Events
        .Where(item => item.Name == name && item.Kind == "Delta" && item.State == "Pending" && !item.Archived).ToArray();

    /// <summary>在不改变来源名称的情况下保存核实统计。</summary>
    private void SaveSources()
    {
        var path = Path.Combine(_root, "sources.json");
        Write(path, _sources);
        _sourceStamp = File.GetLastWriteTimeUtc(path);
    }

    /// <summary>创建有时间和证据的状态事件。</summary>
    private static InventoryEvent AddEvent(InventoryDocument data, string name, string source, string kind, string state,
        long quantity, DateTimeOffset time, string message, long? baseline = null)
    {
        if (data.Events.Count(value => !value.Archived) >= 5000)
            throw new InvalidDataException("未归档事件已达 5000 条，请先处理或归档事件后重试；已有记录未删除。");
        var item = new InventoryEvent { Name = name, Source = source, Kind = kind, State = state,
            Quantity = quantity, RecordedAt = time, Message = message, Baseline = baseline };
        item.History.Add($"{DateTimeOffset.UtcNow:O} EventCreated State={state}\nMessage={TraceMessage(message)}\nName={name}; Source={source}; Baseline={baseline}; Quantity={quantity}; RecordedAt={time:O}");
        data.Events.Add(item);
        return item;
    }

    /// <summary>附加状态历史；事件 ID 不变。</summary>
    private static void Transition(InventoryEvent item, string state, string message, bool archived = false)
    {
        item.State = state;
        if (!archived) item.Message = message;
        item.Archived = archived;
        if (state == "Completed" || archived) item.EndedAt ??= DateTimeOffset.UtcNow;
        item.History.Add($"{DateTimeOffset.UtcNow:O} EventUpdated State={state}; Archived={archived}\nMessage={TraceMessage(message)}");
    }

    /// <summary>核实必须携带可信扫描时刻，且不得早于尚未核实的任何变动。</summary>
    private static string? VerificationError(InventoryDocument data, InventoryReport report, InventoryRecord? candidate)
    {
        var pending = Pending(data, report.Name);
        if (pending.Length == 0 && report.VerifiedAt == null) return null;
        if (string.IsNullOrWhiteSpace(report.VerifiedAt)) return "核实记录缺少可信扫描时间，无法确认当前库存变动。";
        var proof = Parse(new InventoryReport { Quantity = 0, RecordedAt = report.VerifiedAt }, "", DateTimeOffset.UtcNow, out var error);
        if (error != null || proof == null) return "核实扫描时间无效，待后续核实。";
        if (pending.Length > 0 && proof.RecordedAt < pending.Max(item => item.RecordedAt)) return "核实记录已过期，无法确认当前库存变动。";
        if (candidate != null && proof.RecordedAt > candidate.RecordedAt) return "扫描时间晚于库存记录时间，无法完成核实。";
        return null;
    }

    /// <summary>正常扫描接收后核实所有已覆盖变动，推算不一致本身不是来源错误。</summary>
    private static void VerifyPending(InventoryDocument data, string name, InventoryRecord record, bool manual = false)
    {
        var pending = Pending(data, name);
        if (pending.Length == 0) return;
        var expected = (decimal)(pending[0].Baseline ?? 0) + pending.Sum(item => (decimal)item.Quantity);
        var verificationId = record.AttemptId.Length > 0 ? record.AttemptId : Guid.NewGuid().ToString("N");
        foreach (var item in pending)
        {
            item.VerificationSource = record.Source;
            item.VerificationId = verificationId;
            item.VerifiedCorrect = expected == record.Quantity;
            Transition(item, "Completed", "该材料曾有增减信息，已完成核实。" + (manual ? "人工确认。" : ""));
            item.History.Add($"VerificationSource={record.Source}; Quantity={record.Quantity}; VerifiedCorrect={expected == record.Quantity}; RecordedAt={record.RecordedAt:O}");
        }
    }

    /// <summary>增量写入入口；按账号、物品、来源和 UTC 时间去重，不允许人工编辑来源。</summary>
    public Task<InventoryReportResult> ReportDeltaAsync(string uid, AccountRecordSource source, IEnumerable<InventoryDeltaReport> reports)
        => ReportDeltasAsync(uid, reports.Select(report => (source, report)));

    /// <summary>跨来源增量按记录时间稳定排序，并在同一事务中处理。</summary>
    private Task<InventoryReportResult> ReportDeltasAsync(string uid, IEnumerable<(AccountRecordSource Source, InventoryDeltaReport Report)> reports)
    {
        var batch = reports.Select(item => (Source: ValidateSource(item.Source),
            Report: new InventoryDeltaReport { Name = item.Report.Name, Delta = item.Report.Delta, RecordedAt = item.Report.RecordedAt }))
            .OrderBy(item => DateTimeOffset.TryParse(item.Report.RecordedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : DateTimeOffset.MaxValue).ToArray();
        if (batch.Any(item => item.Source.Reference == LocalQuantitySource || item.Source.Reference.Length is 0 or > 1024))
            throw new ArgumentException("增量来源无效，人工编辑不能提交增量。");
        return Locked(() =>
        {
            var data = Load(uid); var settings = Settings(); Snapshot(data, settings);
            var origins = Sources();
            var result = new InventoryReportResult();
            foreach (var entry in batch)
            {
                var report = entry.Report;
                var reference = entry.Source.Reference;
                if (!origins.ContainsKey(reference)) origins[reference] = entry.Source;
                var name = report.Name.Trim();
                var parsed = Parse(new InventoryReport { Quantity = 0, RecordedAt = report.RecordedAt }, reference, DateTimeOffset.UtcNow, out var reason);
                if (string.IsNullOrWhiteSpace(report.RecordedAt)) reason = "增量必须携带时间戳。";
                if (parsed != null && (HasReceipt(data, name, DeltaReceipt(reference, parsed.RecordedAt)) ||
                    data.Events.Any(item => !item.ItemDeleted && item.Kind == "Delta" && item.Name == name && item.Source == reference && item.RecordedAt == parsed.RecordedAt)))
                {
                    result.DuplicateCount++;
                    var repeated = data.Events.FirstOrDefault(item => !item.ItemDeleted && item.Kind == "Delta" && item.Name == name && item.Source == reference && item.RecordedAt == parsed.RecordedAt);
                    if (repeated != null)
                    {
                        if (repeated.RepeatCount < long.MaxValue) repeated.RepeatCount++;
                        repeated.LastReceivedAt = DateTimeOffset.UtcNow;
                        if (!repeated.Archived && repeated.State == "Error") result.DuplicateErrorCount++;
                    }
                    continue;
                }
                data.Items.TryGetValue(name, out var records);
                var baseline = records?.FirstOrDefault();
                if (name.Length is 0 or > 128 || !Definitions().ContainsKey(name)) reason = "增量物品不在字典中。";
                else if (baseline == null) reason = "没有确认库存基准，本次增量不可靠，已拒绝。";
                else if (parsed != null && parsed.RecordedAt < baseline.RecordedAt) reason = "增量早于当前确认库存，已拒绝。";
                var estimate = (decimal)(baseline?.Quantity ?? 0) + Pending(data, name).Sum(item => (decimal)item.Quantity) + report.Delta;
                if (estimate < 0 || estimate > long.MaxValue) reason = "增量推算超出有效库存范围，已拒绝。";
                var item = AddEvent(data, name, reference, "Delta", reason == null ? "Pending" : "Error", report.Delta,
                    parsed?.RecordedAt ?? DateTimeOffset.UtcNow, reason ?? "当前增减仅为推算，待后续核实。", baseline?.Quantity);
                item.History.Add("RecordedAt=" + report.RecordedAt);
                item.History.Add("Current=" + Newtonsoft.Json.JsonConvert.SerializeObject(baseline));
                if (reason == null) result.Accepted.Add(name); else result.Rejected[name] = reason;
            }
            Save(data); SaveSources(); return result;
        });
    }

    /// <summary>查看或保留绿灯提示；只允许明确确认红灯和已完成事件，黄灯不能绕过核实。</summary>
    public Task UpdateEventAsync(string uid, string id, bool keep, bool acknowledge) => Locked(() =>
    {
        var data = Load(uid);
        var item = data.Events.SingleOrDefault(value => value.Id == id) ?? throw new InvalidOperationException("事件不存在。");
        if (keep) { item.KeepNotice = true; item.History.Add($"{DateTimeOffset.UtcNow:O} KeepNotice=True"); }
        else if (acknowledge && !item.Archived && item.State != "Pending")
        {
            if (item.State == "Warning" && item.Kind == "SourceWarning" && Sources().TryGetValue(item.Source, out var source))
            {
                source.WarningTrackingSince = DateTimeOffset.UtcNow;
                SaveSources();
                foreach (var warning in data.Events.Where(value => !value.Archived && value.State == "Warning" && value.Kind == "SourceWarning" && value.Source == item.Source))
                    Transition(warning, warning.State, "已知情，重新统计来源错误。", true);
                PersistInventory(data);
                // 来源统计跨账号共享，因此同一来源的旧提醒也一并结束。
                foreach (var directory in Directory.EnumerateDirectories(_root).Where(path => IsValidUid(Path.GetFileName(path)) && Path.GetFileName(path) != uid))
                {
                    var other = Load(Path.GetFileName(directory));
                    var warnings = other.Events.Where(value => !value.Archived && value.State == "Warning" && value.Kind == "SourceWarning" && value.Source == item.Source).ToArray();
                    if (warnings.Length == 0) continue;
                    foreach (var warning in warnings) Transition(warning, warning.State, "已知情，重新统计来源错误。", true);
                    PersistInventory(other);
                }
                return true;
            }
            if (data.Errors.TryGetValue(item.Name, out var error) && item.History.Any(line => line.Contains(error.Id)))
                throw new InvalidOperationException("请先接受或舍弃当前库存错误，或手动修订库存。");
            Transition(item, item.State, "用户已确认。", true);
        }
        PersistInventory(data);
        return true;
    });

    /// <summary>将尚未处理的红灯事件归档，保留所有历史证据。</summary>
    private static void ArchiveErrors(InventoryDocument data, string name, string reason)
    {
        foreach (var item in data.Events.Where(item => item.Name == name && item.State == "Error" && !item.Archived))
            Transition(item, "Error", reason, true);
    }
}
