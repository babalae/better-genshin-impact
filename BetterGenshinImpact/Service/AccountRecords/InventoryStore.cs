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

    /// <summary>后台执行完整读改写事务；互斥量在同一线程取得并释放。</summary>
    private Task<T> Locked<T>(Func<T> action) => Task.Run(() =>
    {
        using var mutex = new Mutex(false, _mutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("账号记录正在被其他实例更新，请稍后重试。");
            return action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    });

    /// <summary>账号只允许 1～20 位 ASCII 数字，避免路径注入。</summary>
    private string AccountDirectory(string uid)
    {
        if (!Regex.IsMatch(uid, "^[0-9]{1,20}$")) throw new ArgumentException("UID 必须为 1～20 位数字。");
        return Path.Combine(_root, uid);
    }

    /// <summary>按最近记录变动列出 UID；空账号排在后面，读取页面不改变排序。</summary>
    public Task<string[]> ListUidsAsync() => Locked(() => !Directory.Exists(_root) ? [] :
        Directory.EnumerateDirectories(_root).Select(Path.GetFileName)
            .Where(name => name != null && Regex.IsMatch(name, "^[0-9]{1,20}$")).Cast<string>()
            .OrderByDescending(LastModified).ThenBy(name => name, StringComparer.Ordinal).ToArray());

    /// <summary>读取业务变动时间；损坏文件仍保留账号入口，由正式加载显示错误。</summary>
    private DateTimeOffset LastModified(string uid)
    {
        var path = Path.Combine(AccountDirectory(uid), "inventory.json");
        try
        {
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(path))?.ModifiedAt ?? DateTimeOffset.MinValue
                : DateTimeOffset.MinValue;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>显式创建空账号，不生成虚构库存。</summary>
    public Task AddUidAsync(string uid) => Locked(() =>
    {
        var path = Path.Combine(AccountDirectory(uid), "inventory.json");
        if (!File.Exists(path)) Write(path, new InventoryDocument { Uid = uid });
        return true;
    });

    /// <summary>没有账号时也能读取共享显示和保存偏好。</summary>
    public Task<AccountRecordsSettings> GetSettingsAsync() => Locked(Settings);

    /// <summary>读取账号；跨日时备份最后已知状态，不伪造缺失日期。</summary>
    public Task<InventoryWorkspace> LoadAsync(string uid) => Locked(() =>
    {
        var data = Load(uid);
        var settings = Settings();
        Snapshot(data, settings);
        return new InventoryWorkspace
        {
            Inventory = data, Settings = settings, Definitions = Definitions(),
            Sources = Sources().ToDictionary(pair => pair.Key,
                pair => new AccountRecordSource { Reference = pair.Value.Reference, DisplayName = pair.Value.DisplayName }, StringComparer.Ordinal)
        };
    });

    /// <summary>批量局部上报；失败项保留原库存，成功项一起保存。</summary>
    /// <remarks>
    /// 来源由调用方显式提供，不自动追踪调用栈。内置自动判断或修正方法应传入自己的方法标识和可读名称；
    /// 脚本保留实际目录标识，文件导入保留原始来源，不能统一标成人工编辑。
    /// </remarks>
    public Task<InventoryReportResult> ReportAsync(string uid, AccountRecordSource source, IEnumerable<InventoryReport> reports, bool preserveSourceName = false)
    {
        // 复制调用方数据，避免等待锁期间外部继续修改上报集合。
        var batch = reports.Select(report => new InventoryReport { Name = report.Name, Quantity = report.Quantity, RecordedAt = report.RecordedAt }).ToArray();
        var reference = NormalizeSource(source.Reference.Trim());
        var origin = new AccountRecordSource { Reference = reference,
            DisplayName = reference == LocalQuantitySource ? "狛荷屋-人工编辑" : source.DisplayName.Trim() };
        return Locked(() =>
        {
            if (origin.Reference.Length == 0 || origin.Reference.Length > 1024) throw new ArgumentException("来源标识不能为空或超过 1024 字符。");
            var data = Load(uid);
            var settings = Settings();
            var definitions = Definitions();
            Snapshot(data, settings);
            var sources = Sources();
            if (!preserveSourceName || !sources.ContainsKey(origin.Reference)) sources[origin.Reference] = origin;
            Write(Path.Combine(_root, "sources.json"), sources);
            _sourceStamp = File.GetLastWriteTimeUtc(Path.Combine(_root, "sources.json"));
            var now = DateTimeOffset.UtcNow;
            var result = new InventoryReportResult();
            foreach (var group in batch.GroupBy(report => report.Name?.Trim() ?? "", StringComparer.Ordinal))
            {
                var report = group.First();
                report.Name = group.Key;
                data.Items.TryGetValue(group.Key, out var records);
                var current = records?.FirstOrDefault();
                var candidate = Parse(report, origin.Reference, now, out var reason);
                if (group.Skip(1).Any(other => other.Quantity != report.Quantity || other.RecordedAt != report.RecordedAt))
                    reason = "同一批次中存在互相冲突的重复记录。";
                if (!definitions.ContainsKey(group.Key)) reason = "名称不在物品字典中，请手动修订名称或确认补充用户字典。";
                var conflict = reason == null && current != null && candidate != null &&
                    (candidate.RecordedAt < current.RecordedAt || candidate.RecordedAt == current.RecordedAt && candidate.Quantity != current.Quantity);
                if (conflict) reason = "传入记录早于当前记录，或同一时间的数量不同。请选择接受或舍弃，也可右键手动修改数量。";
                if (reason != null)
                {
                    if (data.Errors.Count >= 5000 && !data.Errors.ContainsKey(group.Key))
                        throw new InvalidDataException("未处理异常已达 5000 项，请先处理后再上报。");
                    data.Errors[group.Key] = new InventoryError
                    {
                        Code = conflict ? "RecordConflict" : !definitions.ContainsKey(group.Key) ? "UnknownName" : "Validation",
                        Message = reason, Source = origin.Reference, Incoming = report, Current = current,
                        CanAccept = conflict, OccurredAt = now
                    };
                    result.Rejected[group.Key] = reason;
                    continue;
                }
                Accept(data, group.Key, candidate!, settings.RecentLimit);
                result.Accepted.Add(group.Key);
                // 已有异常需要用户主动处理，正常后台上报不静默清除红灯。
            }
            Save(data);
            return result;
        });
    }

    /// <summary>采集方主动报告识别失败，不制造零库存，也不刷新有效记录时间。</summary>
    public Task ReportFailureAsync(string uid, AccountRecordSource source, string name, string message) => Locked(() =>
    {
        if (string.IsNullOrWhiteSpace(source.Reference) || source.Reference.Length > 1024 || string.IsNullOrWhiteSpace(name) || name.Length > 128 || message.Length > 4096)
            throw new ArgumentException("失败报告的来源、物品名称或说明无效。");
        name = name.Trim();
        var data = Load(uid);
        if (data.Errors.Count >= 5000 && !data.Errors.ContainsKey(name)) throw new InvalidDataException("请先处理已有库存异常。");
        var sources = Sources();
        sources[source.Reference] = new AccountRecordSource { Reference = source.Reference, DisplayName = source.DisplayName };
        Write(Path.Combine(_root, "sources.json"), sources);
        _sourceStamp = File.GetLastWriteTimeUtc(Path.Combine(_root, "sources.json"));
        data.Items.TryGetValue(name, out var records);
        Snapshot(data, Settings());
        data.Errors[name] = new InventoryError
        {
            Code = "ReadFailed", Source = source.Reference, Current = records?.FirstOrDefault(),
            Incoming = new InventoryReport { Name = name },
            Message = $"读取失败：{message}。已保留上次有效库存，请重新采集或右键手动修改。"
        };
        Save(data);
        return true;
    });

    /// <summary>严格解释 UTC/带偏移时间；缺省仅用于新采集上报。</summary>
    private static InventoryRecord? Parse(InventoryReport report, string source, DateTimeOffset now, out string? error)
    {
        error = null;
        var time = now;
        if (report.Quantity < 0) error = "数量不能为负数，请右键手动修改数量。";
        else if (report.RecordedAt != null &&
                 (!Regex.IsMatch(report.RecordedAt.Trim(), @"(?:[zZ]|[+-]\d{2}:?\d{2})$") ||
                  !DateTimeOffset.TryParse(report.RecordedAt, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time)))
            error = "时间无法解析或缺少时区，请手动修改后重新上报。";
        else if (time > now.AddMinutes(5)) error = "记录时间超过当前时间五分钟，请核对来源时钟，或右键手动修改数量以重新确认。";
        return error == null ? new InventoryRecord { Quantity = report.Quantity, RecordedAt = time.ToUniversalTime(), Source = source } : null;
    }

    /// <summary>同一来源替换并移至队首；不按数量变化推断获得量。</summary>
    private static void Accept(InventoryDocument data, string name, InventoryRecord record, int limit)
    {
        if (!data.Items.TryGetValue(name, out var records)) data.Items[name] = records = [];
        records.RemoveAll(item => item.Source == record.Source);
        records.Insert(0, record);
        if (records.Count > limit) records.RemoveRange(limit, records.Count - limit);
    }

    /// <summary>仅处理弹窗看到的异常；接受仍必须通过名称、数量和时间校验。</summary>
    public Task ResolveAsync(string uid, string name, string errorId, bool accept) => Locked(() =>
    {
        var data = Load(uid);
        if (!data.Errors.TryGetValue(name, out var error) || error.Id != errorId)
            throw new InvalidOperationException("异常已被更新，请刷新后重新处理。");
        var settings = Settings();
        Snapshot(data, settings);
        if (accept)
        {
            var record = Parse(error.Incoming, error.Source, DateTimeOffset.UtcNow, out var reason);
            if (!error.CanAccept || record == null || !Definitions().ContainsKey(name))
                throw new InvalidOperationException(reason ?? "请先手动修正名称或字典内容。");
            data.Items.TryGetValue(name, out var records);
            var latest = records?.FirstOrDefault();
            if (JsonConvert.SerializeObject(latest) != JsonConvert.SerializeObject(error.Current))
                throw new InvalidOperationException("当前库存已发生变化，请刷新并手动修改，避免覆盖新的记录。");
            Accept(data, name, record, settings.RecentLimit);
        }
        data.Errors.Remove(name);
        Save(data);
        return true;
    });

    /// <summary>用户明确确认数量，允许替换原记录并解除该物品异常。</summary>
    /// <remarks>
    /// 仅供界面人工编辑使用，来源固定为 AccountRecords.SetLocalQuantityAsync，显示“狛荷屋-人工编辑”。
    /// 后续自动计算、推断、修正库存不得复用此入口，应通过 ReportAsync 显式传入各自来源，避免误标及意外解除待处理异常。
    /// </remarks>
    public Task SetLocalQuantityAsync(string uid, string name, long quantity) => Locked(() =>
    {
        if (quantity < 0 || !Definitions().ContainsKey(name)) throw new ArgumentException("请先确认合法名称，并填写非负整数数量。");
        var data = Load(uid);
        var settings = Settings();
        Snapshot(data, settings);
        var sources = Sources();
        sources[LocalQuantitySource] = new AccountRecordSource { Reference = LocalQuantitySource, DisplayName = "狛荷屋-人工编辑" };
        Write(Path.Combine(_root, "sources.json"), sources);
        _sourceStamp = File.GetLastWriteTimeUtc(Path.Combine(_root, "sources.json"));
        Accept(data, name, new InventoryRecord { Quantity = quantity, RecordedAt = DateTimeOffset.UtcNow, Source = LocalQuantitySource }, settings.RecentLimit);
        data.Errors.Remove(name);
        Save(data);
        return true;
    });

    /// <summary>单独保存用户确认的名称或缺失分类，不覆盖模型已有分类。</summary>
    public Task DefineAsync(string name, string category) => Locked(() =>
    {
        name = name.Trim(); category = category.Trim();
        if (name.Length is 0 or > 128 || category.Length is 0 or > 128) throw new ArgumentException("名称和分类需要填写，且不能超过 128 字符。");
        if (_catalog.Items.TryGetValue(name, out var builtIn) && builtIn.Length != 0)
            throw new InvalidOperationException("模型字典已有分类，不能从界面覆盖。");
        var path = Path.Combine(_root, "item-overrides.json");
        var overrides = Read<Dictionary<string, InventoryItemOverride>>(path);
        overrides[name] = new InventoryItemOverride { Category = category };
        Write(path, overrides);
        return true;
    });

    /// <summary>保存有界配置，并立即收紧当前账号的记录与快照数量。</summary>
    public Task ConfigureAsync(AccountRecordsSettings settings) => Locked(() =>
    {
        settings.RecentLimit = Math.Clamp(settings.RecentLimit, 1, 10);
        settings.SnapshotLimit = Math.Clamp(settings.SnapshotLimit, 1, 30);
        Write(Path.Combine(_root, "settings.json"), settings);
        if (Directory.Exists(_root))
            foreach (var directory in Directory.EnumerateDirectories(_root).Where(path => Regex.IsMatch(Path.GetFileName(path), "^[0-9]{1,20}$")))
            {
                var data = Load(Path.GetFileName(directory));
                foreach (var records in data.Items.Values)
                    if (records.Count > settings.RecentLimit) records.RemoveRange(settings.RecentLimit, records.Count - settings.RecentLimit);
                Write(Path.Combine(directory, "inventory.json"), data);
                PruneSnapshots(directory, settings.SnapshotLimit);
            }
        return true;
    });

    /// <summary>删除物品及其异常、日期快照中的副本。</summary>
    public Task DeleteItemAsync(string uid, string name) => Locked(() =>
    {
        var data = Load(uid);
        data.Items.Remove(name); data.Errors.Remove(name);
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
                var snapshots = Path.Combine(directory, "snapshots");
                if (Directory.Exists(snapshots)) Directory.Delete(snapshots, true);
            }
        }
        if (!deleteUid) Write(Path.Combine(directory, "inventory.json"), new InventoryDocument { Uid = uid });
        return true;
    });

    /// <summary>读取并验证文件，损坏时使用上一次原子保存的备份恢复。</summary>
    private static T Read<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? throw new JsonSerializationException("文件内容为空。"); }
        catch (JsonException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException($"记录文件损坏，请检查：{path}");
            var backup = JsonConvert.DeserializeObject<T>(File.ReadAllText(path + ".bak")) ?? throw new InvalidDataException($"备份也无法读取：{path}");
            File.Copy(path, path + ".corrupt", true);
            File.Copy(path + ".bak", path, true);
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
    private InventoryDocument Load(string uid)
    {
        var data = Read<InventoryDocument>(Path.Combine(AccountDirectory(uid), "inventory.json"));
        if (data.SchemaVersion != 1 || data.Items == null || data.Errors == null || data.Uid == null || data.Uid.Length > 0 && data.Uid != uid ||
            data.Items.Values.Any(records => records == null || records.Any(record => record == null || record.Quantity < 0 || string.IsNullOrEmpty(record.Source))) ||
            data.Errors.Values.Any(error => error == null || error.Incoming == null))
            throw new InvalidDataException("库存格式或 UID 不匹配，请检查记录文件。");
        data.Uid = uid;
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
        data.ModifiedAt = DateTimeOffset.UtcNow;
        Write(Path.Combine(AccountDirectory(data.Uid), "inventory.json"), data);
    }

    /// <summary>读取共享设置，并防止手改文件突破容量上限。</summary>
    private AccountRecordsSettings Settings()
    {
        var settings = Read<AccountRecordsSettings>(Path.Combine(_root, "settings.json"));
        settings.RecentLimit = Math.Clamp(settings.RecentLimit, 1, 10);
        settings.SnapshotLimit = Math.Clamp(settings.SnapshotLimit, 1, 30);
        return settings;
    }

    /// <summary>文件未变时复用来源字典，跨实例更新后重新读取一次。</summary>
    private Dictionary<string, AccountRecordSource> Sources()
    {
        var path = Path.Combine(_root, "sources.json");
        var stamp = File.GetLastWriteTimeUtc(path);
        if (stamp != _sourceStamp)
        {
            _sources = Read<Dictionary<string, AccountRecordSource>>(path);
            _sourceStamp = File.GetLastWriteTimeUtc(path);
        }
        _sources.Remove("Local/AccountRecords");
        _sources.Remove("AccountRecords.LocalEdit");
        _sources[LocalQuantitySource] = new AccountRecordSource { Reference = LocalQuantitySource, DisplayName = "狛荷屋-人工编辑" };
        return _sources;
    }

    /// <summary>原始字典优先，用户补充只填充名称或分类空缺。</summary>
    private Dictionary<string, string> Definitions()
    {
        var definitions = _catalog.Items.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in Read<Dictionary<string, InventoryItemOverride>>(Path.Combine(_root, "item-overrides.json")))
            if (!definitions.TryGetValue(pair.Key, out var category) || category.Length == 0) definitions[pair.Key] = pair.Value.Category;
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
