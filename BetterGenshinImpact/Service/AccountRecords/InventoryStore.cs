using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>库存统一读写入口；跨进程串行化、局部合并、原子保存和有界日期快照。</summary>
public sealed partial class InventoryStore
{
    /// <summary>人工修改来源与实际写入方法对应，显示名称独立维护。</summary>
    private const string LocalQuantitySource = "AccountRecords." + nameof(SetLocalQuantityAsync);

    /// <summary>兼容早期人工编辑来源，避免同一来源在队列中重复。</summary>
    private static string NormalizeSource(string source) => source is "Local/AccountRecords" or "AccountRecords.LocalEdit"
        ? LocalQuantitySource : source;
    /// <summary>统一所有上报入口的来源约束，空显示名回退到实际来源。</summary>
    private static AccountRecordSource ValidateSource(AccountRecordSource source)
    {
        var reference = NormalizeSource(source.Reference?.Trim() ?? "");
        var displayName = string.IsNullOrWhiteSpace(source.DisplayName) ? reference : source.DisplayName.Trim();
        if (reference.Length is 0 or > 1024 || displayName.Length > 1024)
            throw new ArgumentException("来源信息不符合要求：来源标识不能为空，标识和显示名称均不能超过 1024 字符。");
        return new AccountRecordSource { Reference = reference, DisplayName = reference == LocalQuantitySource ? "狛荷屋-人工编辑" : displayName };
    }
    /// <summary>账号数据根目录。</summary>
    private readonly string _root;
    /// <summary>共享模型字典。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>同一数据根的进程间互斥量名称。</summary>
    private readonly string _mutexName;
    /// <summary>当前读取事务中的来源字典，页面不需要逐行读取资源清单。</summary>
    private Dictionary<string, AccountRecordSource> _sources = [];
    /// <summary>来源文件版本，用于检测其他进程的更新。</summary>
    private DateTime _sourceStamp;

    /// <summary>使用当前运行目录下的用户数据。</summary>
    public InventoryStore(InventoryCatalog catalog) : this(catalog, Global.Absolute(@"User\AccountRecords")) { }

    /// <summary>允许测试使用独立数据根。</summary>
    public InventoryStore(InventoryCatalog catalog, string root)
    {
        _catalog = catalog;
        _root = Path.GetFullPath(root);
        _mutexName = "Local\\BetterGI.AccountRecords." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_root.ToUpperInvariant())));
    }

    /// <summary>后台串行执行读改写操作；互斥量在同一线程取得并释放，不提供跨文件回滚。</summary>
    private Task<T> Locked<T>(Func<T> action, bool initialize = true) => Task.Run(() =>
    {
        using var mutex = new Mutex(false, _mutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("账号记录正在被其他操作占用，等待十秒仍未取得访问权限。");
            if (initialize) EnsureSupplementaryDictionary();
            return action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    });

    /// <summary>首次使用时建立补充字典；已有文件（包括空文件）不覆盖，不补回用户删除的条目。</summary>
    private void EnsureSupplementaryDictionary()
    {
        var path = Path.Combine(_root, "item-overrides.json");
        if (File.Exists(path)) return;
        Write(path, CreatePresetCategories());
    }

    /// <summary>首次初始化和手动恢复共用的预置分类，仅包含明确的标准名称，不含赝作。</summary>
    private static Dictionary<string, InventoryItemOverride> CreatePresetCategories() =>
        new(StringComparer.Ordinal)
        {
            ["原石"] = new() { Category = "珍稀货币" },
            ["摩拉"] = new() { Category = "通用货币" },
            ["创世结晶"] = new() { Category = "珍稀货币" },
            ["原粹树脂"] = new() { Category = "树脂" },
            ["浓缩树脂"] = new() { Category = "树脂" },
            ["脆弱树脂"] = new() { Category = "树脂" },
            ["须臾树脂"] = new() { Category = "树脂" }
        };

    /// <summary>恢复预置分类并保留其他条目；在存储锁内校验、合并和保存，已有文件通过 Write 保留备份。</summary>
    public Task RestorePresetCategoriesAsync() => Locked(() =>
    {
        var path = Path.Combine(_root, "item-overrides.json");
        var entries = Read<Dictionary<string, InventoryItemOverride>>(path);
        foreach (var pair in CreatePresetCategories()) entries[pair.Key] = pair.Value;
        Write(path, entries);
        return true;
    });

    /// <summary>用初始预设替换全部账号记录；先准备新目录，替换失败时恢复原目录。</summary>
    public Task ResetToDefaultsAsync() => Locked(() =>
    {
        var replacement = _root + ".reset-" + Guid.NewGuid().ToString("N");
        var backup = _root + ".backup-" + Guid.NewGuid().ToString("N");
        // 预设不包含 UID 或库存；模型字典由资源包提供，补充字典恢复内置条目。
        try
        {
            Write(Path.Combine(replacement, "settings.json"), new AccountRecordsSettings());
            Write(Path.Combine(replacement, "item-overrides.json"), CreatePresetCategories());
            Write(Path.Combine(replacement, "sources.json"), new Dictionary<string, AccountRecordSource>());
            Write(Path.Combine(replacement, "warning-rules.json"), new InventoryWarningRule { InventoryRules = CreateInventoryRuleExamples() });
            Directory.Move(_root, backup);
            try { Directory.Move(replacement, _root); }
            catch
            {
                Directory.Move(backup, _root);
                throw;
            }
            // 原数据保留在独立备份目录，不再被账号列表、快照或字典读取。
            _sources = [];
            _sourceStamp = default;
            _thresholdRuleError = null;
            return true;
        }
        finally
        {
            if (Directory.Exists(replacement)) Directory.Delete(replacement, true);
        }
    });

    /// <summary>游戏 UID 使用九至十位 ASCII 数字，首位不得为零。</summary>
    public static bool IsValidUid(string uid) => Regex.IsMatch(uid, @"\A[1-9][0-9]{8,9}\z");
    /// <summary>先排除非数字路径，再校验 9～10 位 UID，返回账号目录。</summary>
    private string AccountDirectory(string uid)
    {
        if (!Regex.IsMatch(uid, @"\A[0-9]{1,20}\z")) throw new ArgumentException("UID 必须为 9～10 位数字且首位非零。");
        if (!IsValidUid(uid))
            throw new ArgumentException("UID 必须为 9～10 位数字且首位非零。");
        return Path.Combine(_root, uid);
    }

    /// <summary>按最近记录变动列出 UID；空账号排在后面，读取页面不改变排序。</summary>
    public Task<string[]> ListUidsAsync() => Task.Run(() => !Directory.Exists(_root) ? [] :
        Directory.EnumerateDirectories(_root).Select(Path.GetFileName)
            .Where(name => name != null && IsValidUid(name)).Cast<string>()
            .OrderByDescending(LastModified).ThenBy(name => name, StringComparer.Ordinal).ToArray());

    /// <summary>仅读取总表文件修改时间排序账号，不反序列化各账号库存。</summary>
    private DateTimeOffset LastModified(string uid)
    {
        var path = Path.Combine(AccountDirectory(uid), "inventory.json");
        try
        {
            return File.Exists(path)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(path))
                : DateTimeOffset.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>返回页面时只检查总表修改时间，不读取内容、不进入维护或存储写锁。</summary>
    public Task<DateTime> GetInventoryUpdatedAtAsync(string uid) =>
        Task.Run(() => File.GetLastWriteTimeUtc(Path.Combine(AccountDirectory(uid), "inventory.json")));

    /// <summary>显式创建空账号，不生成虚构库存。</summary>
    public Task AddUidAsync(string uid) => Locked(() =>
    {
        if (!IsValidUid(uid)) throw new ArgumentException("UID 必须为 9～10 位数字且首位非零。");
        var path = Path.Combine(AccountDirectory(uid), "inventory.json");
        if (!File.Exists(path)) Write(path, new InventoryDocument { Uid = uid });
        return true;
    });

    /// <summary>没有账号时也能读取共享显示和保存偏好。</summary>
    public Task<AccountRecordsSettings> GetSettingsAsync() => Locked(() => Settings());

    /// <summary>兼容已有调用者：先执行维护，再读取账号；新展示代码显式选择读取或维护。</summary>
    public async Task<InventoryWorkspace> LoadAsync(string uid, bool includeHistory = true)
    {
        await MaintainAsync(uid);
        return await ReadAsync(uid, includeHistory);
    }

    /// <summary>维护日期快照、旧来源迁移和时间提醒；页面进入、主动刷新及分钟检查时显式调用。</summary>
    public Task MaintainAsync(string uid) => Locked(() =>
    {
        Sources();
        var data = Load(uid, includeHistory: false);
        var settings = Settings();
        Snapshot(data, settings);
        if (RefreshThresholdWarnings(data)) PersistInventory(data);
        return true;
    });

    /// <summary>只读取展示所需快照，不初始化文件、不迁移来源、不创建提醒或日期快照。</summary>
    public Task<InventoryWorkspace> ReadAsync(string uid, bool includeHistory = true) => Locked(() =>
    {
        var sources = Sources(migrate: false);
        var data = Load(uid, includeHistory, restoreBackup: false);
        if (!includeHistory) data.Events.RemoveAll(item => item.Archived);
        var workspace = new InventoryWorkspace
        {
            Inventory = data, Settings = Settings(restoreBackup: false), Definitions = Definitions(restoreBackup: false), WarningRuleError = _thresholdRuleError,
            Sources = sources.ToDictionary(pair => pair.Key,
                pair => new AccountRecordSource { Reference = pair.Value.Reference, DisplayName = pair.Value.DisplayName }, StringComparer.Ordinal)
        };
        workspace.UpdatedAt = File.GetLastWriteTimeUtc(Path.Combine(AccountDirectory(uid), "inventory.json"));
        return workspace;
    }, initialize: false);

    /// <summary>批量局部上报；失败项保留原库存，成功项一起保存。</summary>
    /// <remarks>
    /// 来源由调用方显式提供，不自动追踪调用栈。内置自动判断或修正方法应传入自己的方法标识和可读名称；
    /// 脚本保留实际目录标识，文件导入保留原始来源，不能统一标成人工编辑。
    /// </remarks>
    public Task<InventoryReportResult> ReportAsync(string uid, AccountRecordSource source, IEnumerable<InventoryReport> reports, bool preserveSourceName = false)
    {
        // 复制调用方数据，避免等待锁期间外部继续修改上报集合。
        var batch = reports.Select(report => new InventoryReport { Name = report.Name, Category = report.Category, Quantity = report.Quantity, RecordedAt = report.RecordedAt,
            Expirations = report.Expirations?.Select(item => item == null ? null! : new InventoryExpiration { Quantity = item.Quantity, ExpiresAt = item.ExpiresAt }).ToList() }).ToArray();
        var origin = ValidateSource(source);
        // 空名称无法标识物品，在写入任何库存或事件之前拒绝整批上报。
        if (batch.Any(report => string.IsNullOrWhiteSpace(report.Name)))
            throw new ArgumentException("物品名称不能为空或仅包含空白字符，本批上报未保存。");
        return Locked(() =>
        {
            if (origin.Reference.Length == 0 || origin.Reference.Length > 1024) throw new ArgumentException("来源标识为空或超过 1024 字符，无法接收上报。");
            var sources = Sources();
            var data = Load(uid);
            var settings = Settings();
            var definitions = Definitions();
            Snapshot(data, settings);
            if (!preserveSourceName || !sources.ContainsKey(origin.Reference))
            {
                sources[origin.Reference] = origin;
            }
            var attemptId = batch.All(item => item.RecordedAt != null)
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uid + "\n" + origin.Reference + "\n" + JsonConvert.SerializeObject(batch))))
                : Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            var result = new InventoryReportResult();
            foreach (var group in batch.GroupBy(report => report.Name?.Trim() ?? "", StringComparer.Ordinal))
            {
                var report = group.First();
                report.Name = group.Key;
                // 分类提醒与数量校验独立；重复数量上报仍需核对分类，不改写本地字典。
                if (definitions.TryGetValue(group.Key, out var localCategory) && !string.IsNullOrEmpty(localCategory))
                    CheckReportedCategories(data, group.Key, origin.Reference, group.Select(item => item.Category), localCategory, now, result);
                data.Items.TryGetValue(group.Key, out var records);
                var current = records?.FirstOrDefault();
                var candidate = Parse(report, origin.Reference, now, out var reason);
                if (group.Skip(1).Any(other => other.Quantity != report.Quantity || other.RecordedAt != report.RecordedAt || !SameExpirations(other.Expirations, report.Expirations)))
                    reason = "本次上报中，同一物品有多条数量、记录时间或到期信息不一致的记录。请来源合并为一条正确记录后重新上报，或查看游戏后手动填写当前实际库存总数。";
                // 已接收的同来源、同时间、同数量及同到期信息记录不参与本轮冲突或增量核实。
                // 先保留解析与同批冲突检查，避免把不同数量误当作重复丢弃。
                if (reason == null && candidate != null && records?.Any(record =>
                    record.Source == candidate.Source && record.RecordedAt == candidate.RecordedAt && record.Quantity == candidate.Quantity && SameExpirations(record.Expirations, candidate.Expirations)) == true)
                {
                    result.DuplicateCount++;
                    continue;
                }
                if (!definitions.ContainsKey(group.Key)) reason = "物品名称未收录，本次记录未采纳。请改为标准名称，或确认添加到补充字典，再填写游戏中当前实际库存总数。";
                var conflict = reason == null && current != null && candidate != null &&
                    (candidate.RecordedAt < current.RecordedAt || candidate.RecordedAt == current.RecordedAt && (candidate.Quantity != current.Quantity || !SameExpirations(candidate.Expirations, current.Expirations)));
                if (conflict) reason = "本次上报比当前库存记录更早，或同一时间的数量、到期信息不一致，未覆盖当前库存。可舍弃本次异常上报；也可查看游戏后手动填写当前实际库存总数；保存后将归档该物品已有异常。";
                var verificationError = VerificationError(data, report.Name, candidate);
                if (verificationError != null) { reason = verificationError; conflict = false; }
                if (reason != null)
                {
                    var fingerprint = report.RecordedAt == null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        JsonConvert.SerializeObject(new { Source = origin.Reference, report.Name, report.Quantity,
                            RecordedAt = NormalizeReportTime(report.RecordedAt) }) +
                        (report.Expirations == null ? "" : JsonConvert.SerializeObject(report.Expirations)))));
                    if (fingerprint.Length > 0 && HasReceipt(data, group.Key, "Report:" + fingerprint))
                    {
                        result.DuplicateCount++;
                        continue;
                    }
                    var repeated = fingerprint.Length == 0 ? null : data.Events.FirstOrDefault(item => !item.ItemDeleted && item.ReportFingerprint == fingerprint);
                    if (repeated != null)
                    {
                        if (repeated.RepeatCount < long.MaxValue) repeated.RepeatCount++;
                        repeated.LastReceivedAt = now;
                        result.DuplicateCount++;
                        if (!repeated.Archived && repeated.State == "Error") result.DuplicateErrorCount++;
                        continue;
                    }
                    if (data.Errors.Count >= 5000 && !data.Errors.ContainsKey(group.Key))
                        throw new InvalidDataException("待处理库存异常已达 5000 项，暂不能新增异常。请先舍弃异常上报或人工确认当前库存，再重试。");
                    data.Errors[group.Key] = new InventoryError
                    {
                        Code = conflict ? "RecordConflict" : !definitions.ContainsKey(group.Key) ? "UnknownName" : "Validation",
                        Message = reason, Source = origin.Reference, Incoming = report, Current = current,
                        CanAccept = false, OccurredAt = now
                    };
                    result.Rejected[group.Key] = reason;
                    var failure = AddEvent(data, group.Key, origin.Reference, "Report", "Error", report.Quantity,
                        candidate?.RecordedAt ?? now, reason, current?.Quantity);
                    failure.ErrorId = data.Errors[group.Key].Id;
                    failure.History.Add($"Id={data.Errors[group.Key].Id}；AttemptId={attemptId}；RecordedAt={report.RecordedAt}");
                    failure.AttemptId = attemptId;
                    failure.ReportFingerprint = fingerprint;
                    failure.LastReceivedAt = now;
                    failure.History.Add("Current=" + JsonConvert.SerializeObject(current) + "；Incoming=" + JsonConvert.SerializeObject(report));
                    continue;
                }
                candidate!.AttemptId = origin.Reference == LocalQuantitySource ? "" : attemptId;
                VerifyPending(data, group.Key, candidate);
                Accept(data, group.Key, candidate!, settings.RecentLimit);
                var accepted = AddEvent(data, group.Key, origin.Reference, "Report", "Completed", candidate.Quantity,
                    candidate.RecordedAt, "库存上报已接收。", current?.Quantity);
                accepted.AttemptId = attemptId;
                accepted.Expirations = candidate.Expirations;
                accepted.Archived = true;
                accepted.EndedAt = now;
                accepted.History.Add("Incoming=" + JsonConvert.SerializeObject(report));
                result.Accepted.Add(group.Key);
                // 已有异常需要用户主动处理，正常后台上报不静默清除红灯。
            }
            Save(data); SaveSources();
            return result;
        });
    }

    /// <summary>采集方主动报告识别失败，不制造零库存，也不刷新有效记录时间。</summary>
    public Task ReportFailureAsync(string uid, AccountRecordSource source, string name, string message) => Locked(() =>
    {
        source = ValidateSource(source);
        if (string.IsNullOrWhiteSpace(source.Reference) || source.Reference.Length > 1024 || string.IsNullOrWhiteSpace(name) || name.Length > 128 || message.Length > 4096)
            throw new ArgumentException("无法记录采集失败：来源标识须为 1～1024 字符，物品名称须为 1～128 字符，失败说明不能超过 4096 字符。");
        name = name.Trim();
        var sources = Sources();
        var data = Load(uid);
        if (data.Errors.Count >= 5000 && !data.Errors.ContainsKey(name)) throw new InvalidDataException("待处理库存异常已达 5000 项，暂不能记录新的采集失败。请先处理已有异常后重试。");
        if (!sources.ContainsKey(source.Reference)) sources[source.Reference] = new AccountRecordSource { Reference = source.Reference, DisplayName = source.DisplayName };
        var attemptId = Guid.NewGuid().ToString("N");
        Write(Path.Combine(_root, "sources.json"), sources);
        _sourceStamp = File.GetLastWriteTimeUtc(Path.Combine(_root, "sources.json"));
        data.Items.TryGetValue(name, out var records);
        Snapshot(data, Settings());
        data.Errors[name] = new InventoryError
        {
            Code = "ReadFailed", Source = source.Reference, Current = records?.FirstOrDefault(),
            Incoming = new InventoryReport { Name = name },
            Message = $"采集失败：{message}。{(records?.Count > 0 ? "已有确认库存保持不变。" : "当前尚无确认库存，未将失败结果记为零。")}" +
                "请重新采集，或查看游戏后手动填写当前实际库存总数；人工确认后将归档该物品已有异常。"
        };
        var failureEvent = AddEvent(data, name, source.Reference, "Report", "Error", 0, DateTimeOffset.UtcNow, data.Errors[name].Message, records?.FirstOrDefault()?.Quantity);
        failureEvent.ErrorId = data.Errors[name].Id;
        failureEvent.History.Add($"Id={data.Errors[name].Id}；AttemptId={attemptId}");
        failureEvent.AttemptId = attemptId;
        failureEvent.History.Add("Message=" + message);
        failureEvent.History.Add("Current=" + JsonConvert.SerializeObject(records?.FirstOrDefault()));
        Save(data);
        return true;
    });

    /// <summary>严格解释必填的实际记录时间，不以接收时间代替缺失时间。</summary>
    private static InventoryRecord? Parse(InventoryReport report, string source, DateTimeOffset now, out string? error)
    {
        error = null;
        var time = now;
        if (report.Quantity < 0) error = "上报的库存数量为负数，本次记录未采纳。请重新采集，或查看游戏后手动填写当前实际库存总数（不是增减数量）。";
        else if (string.IsNullOrWhiteSpace(report.RecordedAt))
            error = "本次上报缺少记录时间，未更新库存。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。";
        else if (
                 (!Regex.IsMatch(report.RecordedAt.Trim(), @"(?:[zZ]|[+-]\d{2}:?\d{2})$") ||
                  !DateTimeOffset.TryParse(report.RecordedAt, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time)))
            error = "上报的记录时间无法解析或未注明时区。请修正来源或导入文件中的时间后重新上报；也可查看游戏后手动填写当前实际库存总数。";
        else if (time > now.AddMinutes(5)) error = "上报的记录时间比本机时间晚超过五分钟。请核对来源和本机时钟后重新上报，或查看游戏后手动填写当前实际库存总数。";
        if (error == null) error = ValidateExpirations(report.Expirations, report.Quantity);
        return error == null ? new InventoryRecord { Quantity = report.Quantity, RecordedAt = time.ToUniversalTime(), Source = source,
            Expirations = report.Expirations?.Select(item => new InventoryExpiration { Quantity = item.Quantity, ExpiresAt = item.Time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) }).OrderBy(item => item.Time).ThenBy(item => item.Quantity).ToList() } : null;
    }

    /// <summary>分批数量不超过总库存，时间必须显式带时区；允许已过期记录用于追溯。</summary>
    private static string? ValidateExpirations(List<InventoryExpiration>? entries, long quantity)
    {
        if (entries == null) return null;
        if (entries.Count > 100) return "单次上报的到期批次超过 100 条。请来源合并相同到期时间的批次，确保不超过 100 条后重新上报。";
        decimal total = 0;
        foreach (var entry in entries)
        {
            if (entry == null || entry.Quantity <= 0 || string.IsNullOrWhiteSpace(entry.ExpiresAt) ||
                !Regex.IsMatch(entry.ExpiresAt.Trim(), @"(?:[zZ]|[+-]\d{2}:?\d{2})$") ||
                !DateTimeOffset.TryParse(entry.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
                return "到期批次缺少有效数据：每批数量须大于 0，到期时间须可解析并注明时区。请来源修正批次信息后重新上报。";
            total += entry.Quantity;
        }
        return total > quantity ? "到期批次的数量合计大于本次上报的库存总数。请核对总数及各批数量后重新上报。" : null;
    }

    /// <summary>比较分批到期内容，忽略批次排列及等价时区写法。</summary>
    private static bool SameExpirations(List<InventoryExpiration>? left, List<InventoryExpiration>? right) =>
        JsonConvert.SerializeObject(left?.Select(item => item == null ? "null" : $"{item.Quantity}:{NormalizeReportTime(item.ExpiresAt)}").OrderBy(value => value, StringComparer.Ordinal)) ==
        JsonConvert.SerializeObject(right?.Select(item => item == null ? "null" : $"{item.Quantity}:{NormalizeReportTime(item.ExpiresAt)}").OrderBy(value => value, StringComparer.Ordinal));

    /// <summary>同一来源替换并移至队首；不按数量变化推断获得量。</summary>
    private static void Accept(InventoryDocument data, string name, InventoryRecord record, int limit)
    {
        if (!data.Items.TryGetValue(name, out var records)) data.Items[name] = records = [];
        records.RemoveAll(item => item.Source == record.Source);
        records.Insert(0, record);
        if (records.Count > limit) records.RemoveRange(limit, records.Count - limit);
    }

    /// <summary>舍弃异常并保留证据；接受请求一律拒绝，库存只能通过有效上报或人工确认更新。</summary>
    public Task ResolveAsync(string uid, string name, string errorId, bool accept, string? displayedEventId = null) => Locked(() =>
    {
        if (accept) throw new InvalidOperationException("异常记录不能直接接受。请舍弃异常上报，或手动确认当前实际库存总数。");
        var data = Load(uid);
        if (!data.Errors.TryGetValue(name, out var error) || error.Id != errorId)
            throw new InvalidOperationException("此异常已被其他操作更新，当前操作未执行。请刷新后重新选择要处理的异常。");
        var settings = Settings();
        Snapshot(data, settings);
        data.Errors.Remove(name);
        ArchiveErrors(data, name, "用户舍弃异常上报。");
        Save(data);
        return true;
    });

    /// <summary>用户提交当前实际库存，结束此前的待核实事项并归档积压错误；保留已完成核实的证据。</summary>
    /// <remarks>
    /// 仅供界面人工编辑使用，来源固定为 AccountRecords.SetLocalQuantityAsync，显示“狛荷屋-人工编辑”。
    /// 后续自动计算、推断、修正库存不得复用此入口，应通过 ReportAsync 显式传入各自来源，避免误标为人工核实。
    /// </remarks>
    /// <param name="originalName">本次明确修正的未知名称；只结束已有事项，不建立名称映射。</param>
    public Task SetLocalQuantityAsync(string uid, string name, long quantity, string? incorrectAttemptId = null, string? originalName = null) => Locked(() =>
    {
        if (quantity < 0 || !Definitions().ContainsKey(name)) throw new ArgumentException("无法提交人工核实：物品名称须已收录，当前实际库存总数须为非负整数。");
        var sources = Sources();
        var data = Load(uid);
        // 已收录名称不能借人工修订合并；编辑期间原名称被收录时也必须重新确认。
        var renamed = originalName != null && originalName != name;
        if (renamed && Definitions().ContainsKey(originalName!))
            throw new InvalidOperationException("原物品名称已收录，不能按未知名称合并，当前操作未执行。");
        var settings = Settings();
        Snapshot(data, settings);
        if (incorrectAttemptId != null)
        {
            var feedbackName = renamed ? originalName! : name;
            data.Items.TryGetValue(feedbackName, out var previousRecords);
            var original = previousRecords?.FirstOrDefault(record => record.AttemptId == incorrectAttemptId);
            var originalEvent = data.Events.LastOrDefault(item => item.Name == feedbackName && item.AttemptId == incorrectAttemptId);
            var originalSource = original?.Source ?? originalEvent?.Source;
            if (originalSource == null) throw new InvalidOperationException("要标记错误的原上报记录已不存在或超出保留范围。请刷新后重试；也可取消“标记上次上报有错误”，再提交当前实际库存总数。");
            var feedback = AddEvent(data, feedbackName, originalSource, "Report", "Error", quantity, DateTimeOffset.UtcNow, "用户标记上次上报有错误。");
            feedback.History.Add("AttemptId=" + incorrectAttemptId);
            feedback.AttemptId = incorrectAttemptId;
            feedback.History.Add("Current=" + JsonConvert.SerializeObject(previousRecords?.FirstOrDefault()));
        }
        sources[LocalQuantitySource] = new AccountRecordSource { Reference = LocalQuantitySource, DisplayName = "狛荷屋-人工编辑" };
        Write(Path.Combine(_root, "sources.json"), sources);
        _sourceStamp = File.GetLastWriteTimeUtc(Path.Combine(_root, "sources.json"));
        var manual = new InventoryRecord { Quantity = quantity, RecordedAt = DateTimeOffset.UtcNow, Source = LocalQuantitySource };
        Accept(data, name, manual, settings.RecentLimit);
        // 人工总数替代旧推算，不将尚未完成的核实判为成功或失败；同锁内只结束本次提交前的事件。
        foreach (var pending in data.Events.Where(item => (item.Name == name || renamed && item.Name == originalName) && !item.Archived &&
                     item.State == "Pending" && (item.Kind == "Delta" || item.Kind == "ExpirationCheck")))
        {
            Transition(pending, "Aborted", "用户已提交当前实际库存，原待核实事项已终止。", true);
            pending.History.Add($"ManualConfirmation Name={name}; Source={manual.Source}; Quantity={manual.Quantity}; RecordedAt={manual.RecordedAt:O}");
        }
        data.Errors.Remove(name);
        ArchiveErrors(data, name, "用户已提交当前实际库存，历史异常已归档；不代表原上报正确。");
        if (renamed)
        {
            // 原始名称和输入保持不变；同一次库存保存结束原名称下全部积压错误。
            data.Errors.Remove(originalName!);
            ArchiveErrors(data, originalName!, $"用户已将未知名称修正为“{name}”并确认当前库存 {quantity}，历史异常已归档。");
        }
        Save(data);
        return true;
    });

    /// <summary>单独保存用户确认的名称或缺失分类，不覆盖模型已有分类。</summary>
    public Task DefineAsync(string name, string category) => Locked(() =>
    {
        name = name.Trim(); category = category.Trim();
        if (name.Length is 0 or > 128 || category.Length is 0 or > 128) throw new ArgumentException("物品名称和分类均不能为空，且各自不能超过 128 字符。");
        if (_catalog.Items.TryGetValue(name, out var builtIn) && builtIn.Length != 0)
            throw new InvalidOperationException("该物品的分类由内置字典提供，不能在这里覆盖。");
        var path = Path.Combine(_root, "item-overrides.json");
        var overrides = Read<Dictionary<string, InventoryItemOverride>>(path);
        overrides[name] = new InventoryItemOverride { Category = category };
        Write(path, overrides);
        return true;
    });

    /// <summary>保存共享配置，并对所有合法 UID 应用来源记录、归档事件和快照保留上限。</summary>
    public Task ConfigureAsync(AccountRecordsSettings settings) => Locked(() =>
    {
        var previous = Settings();
        settings.RecentLimit = Math.Clamp(settings.RecentLimit, 1, 10);
        settings.SnapshotLimit = Math.Clamp(settings.SnapshotLimit, 1, 30);
        settings.EventLimit = Math.Clamp(settings.EventLimit, 1000, 50000);
        Write(Path.Combine(_root, "settings.json"), settings);
        // 显示偏好只保存设置；只有降低保留上限才需要维护各账号历史。
        if (previous.RecentLimit <= settings.RecentLimit && previous.SnapshotLimit <= settings.SnapshotLimit &&
            previous.EventLimit <= settings.EventLimit) return true;
        if (Directory.Exists(_root))
            foreach (var directory in Directory.EnumerateDirectories(_root).Where(path => IsValidUid(Path.GetFileName(path))))
            {
                var data = Load(Path.GetFileName(directory));
                foreach (var records in data.Items.Values)
                    if (records.Count > settings.RecentLimit) records.RemoveRange(settings.RecentLimit, records.Count - settings.RecentLimit);
                PersistInventory(data);
                PruneSnapshots(directory, settings.SnapshotLimit);
            }
        return true;
    });

    /// <summary>设置当前账号物品的置顶标记，不修改数量、来源或记录时间。</summary>
    public Task SetPriorAsync(string uid, string name, bool prior) => Locked(() =>
    {
        var data = Load(uid);
        if (!data.Items.ContainsKey(name) && !data.Errors.ContainsKey(name) &&
            !data.Events.Any(item => item.Name == name && !item.Archived))
            throw new InvalidOperationException("该物品记录已被删除或不再存在，当前操作未执行。");
        if (prior) data.Prior.Add(name); else data.Prior.Remove(name);
        PersistInventory(data);
        return true;
    });

    /// <summary>删除物品及其异常、日期快照中的副本。</summary>
    public Task DeleteItemAsync(string uid, string name) => Locked(() =>
    {
        var data = Load(uid);
        data.Items.Remove(name); data.Errors.Remove(name);
        data.Prior.Remove(name);
        foreach (var id in data.ThresholdStates.Where(pair => pair.Value.Name == name).Select(pair => pair.Key).ToArray())
            data.ThresholdStates.Remove(id);
        if (data.EventReceipts.TryGetValue(name, out var receipts)) receipts.RemoveWhere(value => !value.StartsWith("Id:", StringComparison.Ordinal));
        foreach (var item in data.Events.Where(item => item.Name == name && !item.ItemDeleted))
        {
            item.ItemDeleted = true;
            Transition(item, item.State, "用户删除物品记录。", true);
        }
        var directory = Path.Combine(AccountDirectory(uid), "snapshots");
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var snapshot = Read<InventoryDocument>(path);
                snapshot.Items.Remove(name); snapshot.Errors.Remove(name); Write(path, snapshot);
                File.Delete(path + ".bak");
            }
        Save(data);
        File.Delete(Path.Combine(AccountDirectory(uid), "inventory.json.bak"));
        return true;
    });

    /// <summary>清空账号库存或全部数据；删除 UID 时一并移除账号目录。</summary>
    public Task ClearAsync(string uid, bool allData, bool deleteUid) => Locked(() =>
    {
        var directory = AccountDirectory(uid);
        if (Directory.Exists(directory))
        {
            if (allData || deleteUid) Directory.Delete(directory, true);
            else
            {
                foreach (var file in new[] { "inventory.json", "inventory.json.bak" }) File.Delete(Path.Combine(directory, file));
                var eventsDirectory = Path.Combine(directory, "events");
                if (Directory.Exists(eventsDirectory)) Directory.Delete(eventsDirectory, true);
                var snapshots = Path.Combine(directory, "snapshots");
                if (Directory.Exists(snapshots)) Directory.Delete(snapshots, true);
            }
        }
        if (!deleteUid) Write(Path.Combine(directory, "inventory.json"), new InventoryDocument { Uid = uid });
        return true;
    });

    /// <summary>读取并验证文件；可编辑字典错误时保留原文，其他文件语法损坏时尝试备份恢复。</summary>
    private static T Read<T>(string path, bool restoreBackup = true) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            var text = NormalizeUserDictionary(path, File.ReadAllText(path));
            ValidateDictionaryFile(path, text);
            return JsonConvert.DeserializeObject<T>(text) ?? throw new JsonSerializationException("文件未包含可读取的数据对象。");
        }
        catch (JsonException)
        {
            if (Path.GetFileName(path) is "sources.json" or "item-overrides.json")
                throw new InvalidDataException($"字典 JSON 无法解析，未自动恢复备份：{path}");
            if (!File.Exists(path + ".bak")) throw new InvalidDataException($"记录文件无法解析，且没有 .bak 备份可供自动恢复：{path}");
            var backupText = NormalizeUserDictionary(path, File.ReadAllText(path + ".bak"));
            ValidateDictionaryFile(path, backupText);
            var backup = JsonConvert.DeserializeObject<T>(backupText) ?? throw new InvalidDataException($"正式记录与备份均无法读取：{path}");
            if (restoreBackup)
            {
                File.Copy(path, path + ".corrupt", true);
                File.Copy(path + ".bak", path, true);
            }
            return backup;
        }
    }

    /// <summary>同目录临时文件完成后原子替换，保留一份故障恢复备份。</summary>
    private static void Write<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(data, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>读取账号内容，未知结构不静默覆盖。</summary>
    private InventoryDocument Load(string uid, bool includeHistory = true, bool restoreBackup = true)
    {
        var data = Read<InventoryDocument>(Path.Combine(AccountDirectory(uid), "inventory.json"), restoreBackup);
        if (data.SchemaVersion != 1 || data.Items == null || data.Errors == null || data.Uid == null || data.Uid.Length > 0 && data.Uid != uid ||
            data.Items.Values.Any(records => records == null || records.Any(record => record == null || record.Quantity < 0 || string.IsNullOrEmpty(record.Source))) ||
            data.Errors.Values.Any(error => error == null || error.Incoming == null))
            throw new InvalidDataException("库存文件的版本、账号或记录结构不符合要求：schemaVersion 须为 1，UID 须与所选账号一致，数量、来源等字段须有效。");
        data.Uid = uid;
        if (data.Prior == null || data.Prior.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("库存文件的 Prior 置顶列表为 null 或包含空物品名，无法载入。");
        if (data.ThresholdStates == null || data.ThresholdStates.Values.Any(state => state == null || state.Name == null || state.Signature == null || state.EventId == null))
            throw new InvalidDataException("库存文件的 ThresholdStates 提醒状态缺少物品名、规则签名或事件标识等必要字段，无法载入。");
        if (data.Items.Values.SelectMany(records => records).Any(record => ValidateExpirations(record.Expirations, record.Quantity) != null))
            throw new InvalidDataException("库存文件中的到期批次不符合数量或时间要求，无法载入。");
        if (data.Events == null || data.Events.Any(item => item == null || item.History == null)) throw new InvalidDataException("库存文件的 Events 列表、事件或 History 历史字段为空，无法载入。");
        if (data.EventReceipts == null || data.EventReceipts.Values.Any(value => value == null)) throw new InvalidDataException("库存文件的 EventReceipts 去重信息为空或包含空条目，无法载入。");
        if (includeHistory) LoadEventArchive(data, restoreBackup);
        foreach (var item in data.Events)
        {
            item.State = InventoryEvent.NormalizeState(item.State, item.Kind);
            InventoryEventCompatibility.RestoreErrorId(item);
        }
        // 来源错误提醒已移除，旧事件归档保留证据，不再参与物品状态展示。
        foreach (var warning in data.Events.Where(item => item.Kind == "SourceWarning" && !item.Archived))
            Transition(warning, "Completed", "来源错误提醒功能已移除。", true);
        foreach (var pair in data.Errors)
            if (!data.Events.Any(item => item.ErrorId == pair.Value.Id))
            {
                var legacy = AddEvent(data, pair.Key, pair.Value.Source, "Report", "Error", pair.Value.Incoming.Quantity, pair.Value.OccurredAt,
                    pair.Value.Message, pair.Value.Current?.Quantity);
                legacy.Id = pair.Value.OccurredAt.UtcDateTime.ToString("yyyyMMdd-HHmmssfffffff") + "-" + pair.Value.Id;
                legacy.ErrorId = pair.Value.Id;
                legacy.History.Add("Id=" + pair.Value.Id);
            }
        foreach (var name in data.Items.Keys.ToArray())
        {
            foreach (var record in data.Items[name]) record.Source = NormalizeSource(record.Source);
            data.Items[name] = data.Items[name].GroupBy(record => record.Source, StringComparer.Ordinal).Select(group => group.First()).ToList();
        }
        foreach (var error in data.Errors.Values)
        {
            error.Source = NormalizeSource(error.Source);
            if (error.Current != null) error.Current.Source = NormalizeSource(error.Current.Source);
        }
        return data;
    }

    /// <summary>保存本地变更时间。</summary>
    private void Save(InventoryDocument data)
    {
        RefreshThresholdWarnings(data);
        data.ModifiedAt = DateTimeOffset.UtcNow;
        PersistInventory(data);
    }

    /// <summary>读取共享设置，并防止手改文件突破容量上限。</summary>
    private AccountRecordsSettings Settings(bool restoreBackup = true)
    {
        var settings = Read<AccountRecordsSettings>(Path.Combine(_root, "settings.json"), restoreBackup);
        settings.RecentLimit = Math.Clamp(settings.RecentLimit, 1, 10);
        settings.SnapshotLimit = Math.Clamp(settings.SnapshotLimit, 1, 30);
        settings.EventLimit = Math.Clamp(settings.EventLimit, 1000, 50000);
        return settings;
    }

    /// <summary>文件未变时复用来源字典，跨实例更新后重新读取一次。</summary>
    private Dictionary<string, AccountRecordSource> Sources(bool migrate = true)
    {
        var path = Path.Combine(_root, "sources.json");
        var stamp = File.GetLastWriteTimeUtc(path);
        if (stamp != _sourceStamp)
        {
            _sources = Read<Dictionary<string, AccountRecordSource>>(path, restoreBackup: migrate);
            _sourceStamp = File.GetLastWriteTimeUtc(path);
        }
        if (migrate)
        {
            MigrateSourceAttempts(_sources);
            _sourceStamp = File.GetLastWriteTimeUtc(path);
        }
        _sources.Remove("Local/AccountRecords");
        _sources.Remove("AccountRecords.LocalEdit");
        _sources[LocalQuantitySource] = new AccountRecordSource { Reference = LocalQuantitySource, DisplayName = "狛荷屋-人工编辑" };
        return _sources;
    }

    /// <summary>原始字典优先，用户补充只填充名称或分类空缺。</summary>
    private Dictionary<string, string> Definitions(bool restoreBackup = true)
    {
        var definitions = _catalog.Items.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        // 用户补充字典优先于内置分类；删除补充条目后自然回退到模型字典。
        var path = Path.Combine(_root, "item-overrides.json");
        var overrides = File.Exists(path) ? Read<Dictionary<string, InventoryItemOverride>>(path, restoreBackup) : CreatePresetCategories();
        foreach (var pair in overrides)
            definitions[pair.Key] = pair.Value.Category;
        return definitions;
    }

    /// <summary>北京时间凌晨四点对应 UTC 前一天二十点，固定偏移不受本机夏令时影响。</summary>
    public static DateOnly RecordDay(DateTimeOffset time) => DateOnly.FromDateTime(time.UtcDateTime.AddHours(4));

    /// <summary>跨日首次读取或写入前保存旧周期的最后库存，每日期仅一份。</summary>
    private void Snapshot(InventoryDocument data, AccountRecordsSettings settings)
    {
        if (!settings.SnapshotsEnabled || data.ModifiedAt == null || data.Items.Count == 0) return;
        var day = RecordDay(data.ModifiedAt.Value);
        if (day >= RecordDay(DateTimeOffset.UtcNow)) return;
        var directory = AccountDirectory(data.Uid);
        var path = Path.Combine(directory, "snapshots", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
        if (!File.Exists(path)) Write(path, new InventoryDocument
        {
            Uid = data.Uid, ModifiedAt = data.ModifiedAt,
            Items = data.Items.Where(pair => pair.Value.Count > 0).ToDictionary(pair => pair.Key, pair => new List<InventoryRecord> { pair.Value[0] }, StringComparer.Ordinal)
        });
        PruneSnapshots(directory, settings.SnapshotLimit);
    }

    /// <summary>按有记录日期计数，不按距今天的天数删除最后有效快照。</summary>
    private static void PruneSnapshots(string accountDirectory, int limit)
    {
        var directory = Path.Combine(accountDirectory, "snapshots");
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(limit))
        {
            File.Delete(path); File.Delete(path + ".bak");
        }
    }
}
