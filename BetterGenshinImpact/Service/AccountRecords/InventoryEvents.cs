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

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>增量、核实和事件归档；所有操作通过同一存储锁串行化。</summary>
public sealed partial class InventoryStore
{
    /// <summary>新的有效总库存核实数量后，将历史错误归档，并合并为可确认的橘灯提醒。</summary>
    private static void ConfirmPreviousErrors(InventoryDocument data, string name, InventoryRecord record)
    {
        var errors = data.Events.Where(item => item.Name == name && item.State == "Error" && !item.Archived).ToArray();
        if (errors.Length == 0 && !data.Errors.ContainsKey(name)) return;
        data.Errors.Remove(name);
        foreach (var error in errors)
        {
            error.VerificationSource = record.Source;
            error.VerificationId = record.AttemptId;
            Transition(error, "Error", "当前数量已由新的库存上报核实，原错误转为历史提醒。", true);
        }
        const string message = "该材料曾有一条或多条错误，当前库存数量已核实。原始错误已保留在历史记录中，请确认后归档此提醒。";
        var warning = data.Events.FirstOrDefault(item => item.Name == name && item.Kind == "VerifiedErrorWarning" && !item.Archived);
        if (warning == null)
            warning = AddEvent(data, name, record.Source, "VerifiedErrorWarning", "Warning", record.Quantity, record.RecordedAt, message);
        else
        {
            warning.Source = record.Source;
            warning.Quantity = record.Quantity;
            warning.RecordedAt = record.RecordedAt;
            Transition(warning, "Warning", message);
        }
        warning.VerificationSource = record.Source;
        warning.VerificationId = record.AttemptId;
        warning.History.Add($"ConfirmedQuantity={record.Quantity}; RecordedAt={record.RecordedAt:O}; ErrorIds={string.Join(",", errors.Select(item => item.Id))}");
    }

    /// <summary>按物品和来源更新分类橘灯；分类冲突不修改字典、不参与数量拒绝或增量核实。</summary>
    private static void CheckReportedCategories(InventoryDocument data, string name, string source,
        IEnumerable<string?> categories, string localCategory, DateTimeOffset now, InventoryReportResult result)
    {
        var supplied = categories.Where(value => value != null).Select(value => value!.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (supplied.Length == 0) return;
        var warning = data.Events.FirstOrDefault(item => !item.Archived && !item.ItemDeleted &&
            item.Name == name && item.Source == source && item.Kind == "CategoryWarning");
        if (supplied.All(value => value == localCategory))
        {
            // 只有明确上报一致分类才结束提醒；省略分类不代表冲突已解决。
            if (warning != null) Transition(warning, "Completed", "本次上报分类与本地一致，分类提醒已结束。", true);
            return;
        }

        var invalid = supplied.Any(value => value.Length is 0 or > 128);
        var incoming = invalid ? "无效分类" : string.Join("、", supplied.Take(5));
        var message = $"来源上报分类“{incoming}”与本地分类“{localCategory}”不一致或无效，已保留本地分类；数量独立校验，结果请查看上报反馈。";
        result.Warnings[name] = message;
        if (warning == null)
            warning = AddEvent(data, name, source, "CategoryWarning", "Warning", 0, now, message);
        else
        {
            if (warning.RepeatCount < long.MaxValue) warning.RepeatCount++;
            warning.Message = message;
            warning.RecordedAt = now;
        }
        warning.LastReceivedAt = now;
        // 只保留创建记录及最近一次分类证据，反复上报不无限追加历史。
        if (warning.History.Count > 1) warning.History.RemoveRange(1, warning.History.Count - 1);
        warning.History.Add($"{now:O} CategoryChecked Source={source}; LocalCategory={localCategory}; IncomingCategory={incoming}; RepeatCount={warning.RepeatCount}");
    }

    /// <summary>仅归档用户选定范围内、提交时仍未受阻的同类完成事件，不纳入后来出现的新事件。</summary>
    public Task<int> ArchiveCompletedEventsAsync(string uid, string kind, IReadOnlyCollection<string> eventIds) => Locked(() =>
    {
        var data = Load(uid);
        // 到期等时间条件也可能在用户打开提示之后变化，需要先更新再确认资格。
        var changed = RefreshThresholdWarnings(data);
        var selected = eventIds.ToHashSet(StringComparer.Ordinal);
        var completed = InventoryEventPolicy.GetCompletedArchiveCandidates(data, kind).Where(item => selected.Contains(item.Id)).ToArray();
        foreach (var item in completed) Transition(item, item.State, "用户已批量归档。", true);
        if (completed.Length > 0 || changed) PersistInventory(data);
        return completed.Length;
    });
    /// <summary>取得尚未核实的增量；绿灯保留并不代表增量仍待核实。</summary>
    private static InventoryEvent[] Pending(InventoryDocument data, string name) => data.Events
        .Where(item => item.Name == name && item.Kind == "Delta" && item.State == "Pending" && !item.Archived).ToArray();

    /// <summary>保存来源名称映射；上报证据由事件记录承载。</summary>
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
        if (state != "Completed" && data.Events.Count(value => !value.Archived && value.State != "Completed") >= 5000)
            throw new InvalidDataException("未归档且尚未完成的事件已达 5000 条，暂不能新增此类事件。请核实待确认变动、处理错误或归档可归档的提醒后重试；仅归档已完成事件不会释放此额度。");
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

    /// <summary>使用已校验的实际记录时间核实增量，不得早于尚未核实的任何变动。</summary>
    private static string? VerificationError(InventoryDocument data, string name, InventoryRecord? candidate)
    {
        if (candidate == null) return null;
        var pending = Pending(data, name);
        if (pending.Length > 0 && candidate.RecordedAt == pending.Max(item => item.RecordedAt)) return "库存与待核实变动的记录时间相同，无法判断先后。请上报实际采集时间更晚的库存，并保留时间精度。";
        if (pending.Length > 0 && candidate.RecordedAt < pending.Max(item => item.RecordedAt)) return "本次上报的记录时间早于待核实的库存变动，不能确认变动后的数量。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。";
        return null;
    }

    /// <summary>有效库存上报接收后核实所有已覆盖变动，推算不一致本身不是来源错误。</summary>
    private static void VerifyPending(InventoryDocument data, string name, InventoryRecord record)
    {
        var pending = Pending(data, name);
        if (pending.Length == 0) return;
        // 无基准的变动由本次总库存覆盖，不能假设原库存为零来比对。
        decimal? expected = pending[0].Baseline is { } baseline
            ? (decimal)baseline + pending.Sum(item => (decimal)item.Quantity) : null;
        var verificationId = record.AttemptId.Length > 0 ? record.AttemptId : Guid.NewGuid().ToString("N");
        foreach (var item in pending)
        {
            item.VerificationSource = record.Source;
            item.VerificationId = verificationId;
            item.VerifiedCorrect = expected.HasValue ? expected.Value == record.Quantity : null;
            Transition(item, "Completed", "该材料曾有增减信息，已完成核实。");
            item.History.Add($"VerificationSource={record.Source}; Quantity={record.Quantity}; VerifiedCorrect={item.VerifiedCorrect}; RecordedAt={record.RecordedAt:O}");
        }
    }

    /// <summary>增量写入入口；按账号、物品、来源和 UTC 时间去重，不允许人工编辑来源。</summary>
    public Task<InventoryReportResult> ReportDeltaAsync(string uid, AccountRecordSource source, IEnumerable<InventoryDeltaReport> reports)
        => ReportDeltasAsync(uid, reports.Select(report => (source, report)));

    /// <summary>跨来源增量按记录时间稳定排序，在同一次存储锁操作中处理并保存。</summary>
    private Task<InventoryReportResult> ReportDeltasAsync(string uid, IEnumerable<(AccountRecordSource Source, InventoryDeltaReport Report)> reports)
    {
        var batch = reports.Select(item => (Source: ValidateSource(item.Source),
            Report: new InventoryDeltaReport { Name = item.Report.Name, Delta = item.Report.Delta, RecordedAt = item.Report.RecordedAt,
                Expirations = item.Report.Expirations?.Select(value => value == null ? null! : new InventoryExpiration { Quantity = value.Quantity, ExpiresAt = value.ExpiresAt }).ToList() }))
            .OrderBy(item => DateTimeOffset.TryParse(item.Report.RecordedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : DateTimeOffset.MaxValue).ToArray();
        // 与完整库存上报一致，空名称在持久化前拒绝，不能聚合成空名称错误行。
        if (batch.Any(item => string.IsNullOrWhiteSpace(item.Report.Name)))
            throw new ArgumentException("物品名称不能为空或仅包含空白字符，本批上报未保存。");
        if (batch.Any(item => item.Source.Reference == LocalQuantitySource || item.Source.Reference.Length is 0 or > 1024))
            throw new ArgumentException("人工编辑来源只能提交当前库存总数，不能提交增减数量。请自动上报方使用自己的来源标识。");
        return Locked(() =>
        {
            var origins = Sources();
            var data = Load(uid); var settings = Settings(); Snapshot(data, settings);
            var result = new InventoryReportResult();
            foreach (var entry in batch)
            {
                var report = entry.Report;
                var reference = entry.Source.Reference;
                if (!origins.ContainsKey(reference)) origins[reference] = entry.Source;
                var name = report.Name.Trim();
                var attemptId = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(uid + "\n" + reference + "\n" + JsonConvert.SerializeObject(report))));
                var parsed = Parse(new InventoryReport { Quantity = 0, RecordedAt = report.RecordedAt }, reference, DateTimeOffset.UtcNow, out var reason);
                if (string.IsNullOrWhiteSpace(report.RecordedAt)) reason = "库存变动缺少发生时间，本次增减未纳入推算。请来源补充带时区的发生时间后重新上报。";
                // 只有已接收事件约束再次累加；失败事件必须重新校验，支持修正输入和补充字典后重试。
                var related = data.Events.Where(item => !item.ItemDeleted && item.Kind == "Delta" && item.Name == name &&
                    item.Source == reference && (parsed != null ? item.RecordedAt == parsed.RecordedAt : item.AttemptId == attemptId)).ToArray();
                var accepted = related.FirstOrDefault(item => item.State != "Error");
                var receiptPrefix = parsed == null ? "" : DeltaReceiptPrefix(reference, parsed.RecordedAt);
                var savedReceipt = parsed == null ? null : data.EventReceipts.GetValueOrDefault(name)?.FirstOrDefault(value => value.StartsWith(receiptPrefix, StringComparison.Ordinal));
                var legacyReceipt = parsed != null && HasReceipt(data, name, DeltaReceipt(reference, parsed.RecordedAt));
                var sameAccepted = accepted != null && accepted.Quantity == report.Delta && SameExpirations(accepted.Expirations, report.Expirations);
                if (sameAccepted || savedReceipt == receiptPrefix + DeltaContentKey(report.Delta, report.Expirations) ||
                    legacyReceipt && related.Length == 0 && savedReceipt == null)
                {
                    result.DuplicateCount++;
                    if (accepted != null)
                    {
                        if (accepted.RepeatCount < long.MaxValue) accepted.RepeatCount++;
                        accepted.LastReceivedAt = DateTimeOffset.UtcNow;
                    }
                    continue;
                }
                data.Items.TryGetValue(name, out var records);
                var baseline = records?.FirstOrDefault();
                if (name.Length is 0 or > 128 || !Definitions().ContainsKey(name)) reason = "库存变动的物品名称为空、超过 128 字符或未收录，本次增减未纳入推算。请修正名称或补充物品字典后重新上报。";
                else if (baseline != null && parsed != null && parsed.RecordedAt == baseline.RecordedAt) reason = "库存变动与确认库存的记录时间相同，无法判断先后，本次增减未纳入推算。请来源保留实际发生时间及足够精度。";
                else if (baseline != null && parsed != null && parsed.RecordedAt < baseline.RecordedAt) reason = "本次变动早于当前确认库存，可能已包含在该库存中，因此未纳入推算。请核对发生时间，勿重复累计；如数量存疑，请查看游戏后手动填写当前实际库存总数。";
                var pending = Pending(data, name);
                // 无确认库存时保留有符号变动，不创建零库存基准；范围校验仅适用于已有基准。
                // TODO：需求验证可依据增量判断达标（如 +5）；涉及负数时应报错，留待需求模块实现。
                if (reason == null && baseline != null && parsed != null &&
                    !HasValidDeltaSequence(baseline, pending, report, parsed.RecordedAt))
                    reason = "按发生时间累计变动后，数量超出有效库存范围（小于 0 或超过 64 位整数上限），本次增减未纳入推算。请核对变动记录，或查看游戏后手动填写当前实际库存总数。";
                if (reason == null) reason = ValidateDeltaExpirations(report, baseline, pending);
                if (accepted != null || savedReceipt != null)
                    reason = "同一物品、来源和发生时间的增量已接收，但本次内容不同。请核对来源；如需修正库存，请上报新的实际库存总数，不能重复累加或覆盖已接收增量。";
                // 完全相同的失败合并次数；成功重试只结束同一事件的错误，不清除其他库存异常。
                var repeatedError = related.FirstOrDefault(item => item.State == "Error" && item.Quantity == report.Delta &&
                    SameExpirations(item.Expirations, report.Expirations));
                if (reason != null && repeatedError != null)
                {
                    if (repeatedError.RepeatCount < long.MaxValue) repeatedError.RepeatCount++;
                    repeatedError.LastReceivedAt = DateTimeOffset.UtcNow;
                    if (!repeatedError.Archived && repeatedError.Message != reason)
                        Transition(repeatedError, "Error", reason);
                    result.DuplicateCount++;
                    if (!repeatedError.Archived) result.DuplicateErrorCount++;
                    continue;
                }
                if (reason == null)
                    foreach (var error in related.Where(item => item.State == "Error" && !item.Archived))
                        Transition(error, "Error", "同一增量已重新校验并接收，原错误已修正。", true);
                var item = AddEvent(data, name, reference, "Delta", reason == null ? "Pending" : "Error", report.Delta,
                    parsed?.RecordedAt ?? DateTimeOffset.UtcNow, reason ?? "当前增减仅为推算，待后续核实。", baseline?.Quantity);
                // 增量也保存稳定的上报标识，不改变原有按来源和时间去重的规则。
                item.AttemptId = attemptId;
                item.History.Add("RecordedAt=" + report.RecordedAt);
                item.Expirations = report.Expirations;
                item.History.Add("Current=" + Newtonsoft.Json.JsonConvert.SerializeObject(baseline));
                if (reason == null) result.Accepted.Add(name); else result.Rejected[name] = reason;
            }
            Save(data); SaveSources(); return result;
        });
    }

    /// <summary>保留或归档事件；Pending 不允许直接归档，有未处理库存错误时须先解决冲突。</summary>
    public Task UpdateEventAsync(string uid, string id, bool keep, bool acknowledge) => Locked(() =>
    {
        var data = Load(uid);
        var item = data.Events.SingleOrDefault(value => value.Id == id) ?? throw new InvalidOperationException("该事件已不存在，可能已被清理。请刷新事件列表后重新选择。");
        if (keep) { item.KeepNotice = true; item.History.Add($"{DateTimeOffset.UtcNow:O} KeepNotice=True"); }
        else if (acknowledge && !item.Archived && item.State != "Pending")
        {
            if (data.Errors.TryGetValue(item.Name, out var error) && item.ErrorId == error.Id)
                throw new InvalidOperationException("此异常尚未处理，不能直接归档。请舍弃异常上报；也可查看游戏后手动填写当前实际库存总数，保存后将归档该物品已有异常。");
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
