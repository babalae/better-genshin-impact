using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using BetterGenshinImpact.Core.Script.Utils;
using BetterGenshinImpact.Core.Material;
using BetterGenshinImpact.GameTask.Model.Area;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Microsoft.ClearScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>每个 JS 引擎独立的库存入口；业务校验由库存服务处理。</summary>
public sealed class ScriptInventory
{
    /// <summary>共享库存服务。</summary>
    private readonly InventoryStore _store;
    /// <summary>宿主确定的来源及批次。</summary>
    private readonly ScriptInventoryContext _context;
    /// <summary>传统入口首次访问时读取 UID，不扫描背包。</summary>
    private readonly Func<string?> _readUid;
    /// <summary>任务退出后不再接收调用。</summary>
    private readonly CancellationToken _ct;
    /// <summary>脚本工作目录，图标只允许从该目录读取。</summary>
    private readonly string? _workDirectory;
    /// <summary>跨账号共享的图标缓存。</summary>
    private readonly InventoryIconCache? _icons;
    /// <summary>本轮图标识别能力，不依赖账号。</summary>
    private readonly ScriptInventoryIconRecognizer? _iconRecognizer;
    /// <summary>串行化 JS 调用，保证记录编号及提交顺序。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>本次执行绑定的游戏 UID。</summary>
    private string? _uid;
    /// <summary>本次执行跨提交和分组递增的记录编号，切换分组不复用编号。</summary>
    private long _recordNumber;
    /// <summary>输出原生 JS 对象所用的字段命名。</summary>
    private static readonly JsonSerializerSettings OutputSettings = new()
    { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } };

    /// <summary>注入存储和身份解析，不在宿主对象内查找全局服务。</summary>
    public ScriptInventory(InventoryStore store, ScriptInventoryContext context, Func<string?> readUid, CancellationToken ct,
        string? workDirectory = null, InventoryIconCache? icons = null, ScriptInventoryIconRecognizer? iconRecognizer = null)
    {
        _store = store;
        _context = context;
        _readUid = readUid;
        _ct = ct;
        _workDirectory = workDirectory == null ? null : Path.GetFullPath(workDirectory);
        _icons = icons;
        _iconRecognizer = iconRecognizer;
        _uid = context.VerifiedUid;
    }

    /// <summary>注册原生 JS 入口，数组和返回值不暴露 CLR 集合。</summary>
    internal static void Register(IScriptEngine engine, ScriptInventory inventory)
    {
        engine.AddHostObject("__bgiInventory", inventory);
        engine.Execute("""
            (() => {
                const host = __bgiInventory;
                globalThis.inventory = Object.freeze({
                    report: async (records, options = {}) => {
                        if (!Array.isArray(records)) throw new TypeError('inventory.report 需要记录数组');
                        if (options === null || typeof options !== 'object' || Array.isArray(options))
                            throw new TypeError('inventory.report 的 options 需要对象');
                        for (const item of records) {
                            if (item !== null && typeof item === 'object' && 'targetQuantity' in item &&
                                (!Number.isSafeInteger(item.targetQuantity) || item.targetQuantity < 0))
                                throw new TypeError('targetQuantity 必须是非负安全整数');
                        }
                        return JSON.parse(await host.Report(JSON.stringify(records), JSON.stringify(options)));
                    },
                    getQuantities: async names => {
                        if (typeof names !== 'string' && !Array.isArray(names))
                            throw new TypeError('inventory.getQuantities 需要名称或名称数组');
                        return JSON.parse(await host.GetQuantities(JSON.stringify(names)));
                    },
                    identifyIcon: image => JSON.parse(host.IdentifyIcon(image)),
                    getMaterialFamily: async name => {
                        if (typeof name !== 'string' || !name.trim())
                            throw new TypeError('inventory.getMaterialFamily 需要非空材料名称');
                        return JSON.parse(host.GetMaterialFamily(name));
                    },
                    getDemandSettings: async () => JSON.parse(await host.GetDemandSettings()),
                    getReportDates: async names => {
                        if (typeof names !== 'string' && !Array.isArray(names))
                            throw new TypeError('inventory.getReportDates 需要名称或名称数组');
                        return JSON.parse(await host.GetReportDates(JSON.stringify(names)));
                    },
                    getIssueNames: async () => JSON.parse(await host.GetIssueNames()),
                    getMissingIconNames: async (filters = {}) => {
                        if (filters === null || typeof filters !== 'object' || Array.isArray(filters))
                            throw new TypeError('inventory.getMissingIconNames 需要筛选对象');
                        return JSON.parse(await host.GetMissingIconNames(JSON.stringify(filters)));
                    }
                });
            })();
            """);
    }

    /// <summary>识别单个裁剪图标；不识别 UID，也不修改库存或图像缓存。</summary>
    public string IdentifyIcon(ImageRegion image) => (_iconRecognizer
        ?? throw new InvalidOperationException("未配置狛荷屋图标识别服务。")).Identify(image);

    /// <summary>查询静态升级家族；成员按低到高排列，无家族返回 null，不读取账号或库存。</summary>
    public string GetMaterialFamily(string name)
    {
        using var operation = ScriptHostOperations.Enter();
        _ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("材料名称不能为空。", nameof(name));
        var family = MaterialFamilyCatalog.Families.FirstOrDefault(item => item.Members.Contains(name.Trim()));
        return JsonConvert.SerializeObject(family, OutputSettings);
    }

    /// <summary>接收数组；结构先整体校验，业务失败按记录返回并保留追踪。</summary>
    public async Task<string> Report(string json, string optionsJson = "{}")
    {
        using var operation = ScriptHostOperations.Enter();
        _ct.ThrowIfCancellationRequested();
        var input = Parse(json) as JArray ?? throw new ArgumentException("库存上报必须是数组。");
        // 纯图标记录先校验结构，再从数量/需求处理链分离；不伪造数量或读取账号。
        var iconOnly = input.OfType<JObject>().Where(item => item.ContainsKey("iconPath") &&
            !item.ContainsKey("quantity") && !item.ContainsKey("targetQuantity")).ToArray();
        foreach (var item in iconOnly)
        {
            if (item["name"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)item["name"]) ||
                ((string)item["name"]!).Trim().Length > 128 || item.Properties().Any(p => p.Name is not ("name" or "iconPath")))
                throw new ArgumentException("纯图标记录仅支持非空 name（最多 128 字符）和 iconPath。");
        }
        // 图标是可选汇报项，错误不能阻断同条数量和需求的正常接收。
        var attachments = input.OfType<JObject>().Where(item => item.ContainsKey("iconPath"))
            .Select(item => (Input: item, Path: item["iconPath"]!.DeepClone(), OnlyIcon: iconOnly.Contains(item))).ToArray();
        foreach (var item in iconOnly) item.Remove();
        foreach (var item in input.OfType<JObject>()) item.Remove("iconPath");
        // 先完整校验目标和库存结构；仅上报目标不生成库存记录。
        var targets = ParseDemandTargets(input);
        // 图标随所属输入的接收结果处理，不能借用同名另一条记录的成功结果。
        var recordsByInput = new Dictionary<JObject, InventoryReport>();
        var reports = input.Cast<JObject>().Where(item => item.ContainsKey("quantity")).Select(item =>
        {
            var inventory = (JObject)item.DeepClone();
            inventory.Remove("targetQuantity");
            var parsed = ParseRecord(inventory);
            recordsByInput.Add(item, parsed.Report);
            return parsed;
        }).ToArray();
        // 分组参数在任何写入前校验；不把拼错的参数静默归入默认批次。
        if (Parse(optionsJson) is not JObject options || options.Properties().Any(p => p.Name != "batch") ||
            options["batch"] is { Type: not JTokenType.String })
            throw new ArgumentException("options 仅支持可选的字符串 batch 分组名。");
        var batchId = _context.GetBatchId((string?)options["batch"]);
        await _gate.WaitAsync(_ct).ConfigureAwait(false);
        try
        {
            _ct.ThrowIfCancellationRequested();
            var uid = reports.Length > 0 || targets.Count > 0 ? ResolveUid() : null;
            var result = new InventoryReportResult { BatchId = batchId };
            var source = new AccountRecordSource { Reference = _context.Source, DisplayName = _context.DisplayName };
            // 按数组顺序处理，不按类型分组，也不按名称去重。
            foreach (var (report, _) in reports)
                report.RecordId = checked(++_recordNumber).ToString(CultureInfo.InvariantCulture);
            for (var index = 0; index < reports.Length; index++)
            {
                _ct.ThrowIfCancellationRequested();
                var (report, delta) = reports[index];
                // 不同物品的连续实测一次保存；同名实测必须分开，避免触发底层单次重复名称校验。
                var snapshots = new List<InventoryReport> { report };
                var names = new HashSet<string>(StringComparer.Ordinal) { report.Name.Trim() };
                if (!delta)
                    while (index + 1 < reports.Length && !reports[index + 1].Delta &&
                        names.Add(reports[index + 1].Report.Name.Trim()))
                        snapshots.Add(reports[++index].Report);
                var part = delta
                    ? await _store.ReportDeltaAsync(uid!, source, [new InventoryDeltaReport
                    { RecordId = report.RecordId, Name = report.Name, Delta = report.Quantity,
                        RecordedAt = report.RecordedAt!, Expirations = report.Expirations }], batchId).ConfigureAwait(false)
                    : await _store.ReportAsync(uid!, source, snapshots, batchId: batchId).ConfigureAwait(false);
                result.Records.AddRange(part.Records);
                result.Accepted.AddRange(part.Accepted);
                foreach (var pair in part.Rejected) result.Rejected[pair.Key] = pair.Value;
                foreach (var pair in part.Warnings) result.Warnings[pair.Key] = pair.Value;
                result.DuplicateCount += part.DuplicateCount;
                result.DuplicateErrorCount += part.DuplicateErrorCount;
            }
            // 库存提交完成后再登记目标，使新需求以最新库存为依据；库存异常时不继续写需求。
            _ct.ThrowIfCancellationRequested();
            var demandResult = targets.Count > 0
                ? await _store.ReportDemandsAsync(uid!, source, targets).ConfigureAwait(false) : null;
            var cacheSettings = attachments.Length > 0 ? await _store.GetSettingsAsync().ConfigureAwait(false) : null;
            var acceptedRecords = result.Records.Where(item => item.Outcome is "Accepted" or "Duplicate")
                .Select(item => item.RecordId).ToHashSet(StringComparer.Ordinal);
            foreach (var (item, path, onlyIcon) in attachments)
            {
                _ct.ThrowIfCancellationRequested();
                var name = ((string)item["name"]!).Trim();
                if (cacheSettings?.UseImageCache != true)
                {
                    if (onlyIcon) result.Rejected[name] = "图像缓存未开启。";
                    continue;
                }
                if (!onlyIcon && !(recordsByInput.TryGetValue(item, out var record) && acceptedRecords.Contains(record.RecordId!) ||
                    item.ContainsKey("targetQuantity") && demandResult?.Accepted.Contains(name) == true)) continue;
                try
                {
                    StoreIcon(name, path, cacheSettings.IconCacheSize);
                    if (onlyIcon) result.Accepted.Add(name);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or
                    NotSupportedException or InvalidOperationException or OpenCvSharp.OpenCVException)
                {
                    var warning = "图标缓存未更新：" + ex.Message;
                    if (onlyIcon) { result.Rejected[name] = warning; continue; }
                    result.Warnings[name] = result.Warnings.TryGetValue(name, out var previous)
                        ? previous + "；" + warning : warning;
                }
            }
            var output = JObject.FromObject(result, JsonSerializer.Create(OutputSettings));
            if (demandResult != null)
            {
                output["demandsAccepted"] = JArray.FromObject(demandResult.Accepted);
                output["demandsRejected"] = JObject.FromObject(demandResult.Rejected);
                output["demandsWarnings"] = JObject.FromObject(demandResult.Warnings);
            }
            return output.ToString(Formatting.None);
        }
        finally { _gate.Release(); }
    }

    /// <summary>查询当前账号库存缺图名称；名称、分类及背包页筛选取交集，未传表示不限。</summary>
    public async Task<string> GetMissingIconNames(string filtersJson = "{}")
    {
        using var operation = ScriptHostOperations.Enter();
        _ct.ThrowIfCancellationRequested();
        if (Parse(filtersJson) is not JObject filters || filters.Properties().Any(p => p.Name is not ("names" or "categories" or "pages")))
            throw new ArgumentException("筛选仅支持 names、categories、pages。");
        var selected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var property in filters.Properties())
        {
            var values = property.Value.Type == JTokenType.String ? new JArray(property.Value) : property.Value as JArray;
            if (values == null || values.Any(value => value.Type != JTokenType.String))
                throw new ArgumentException($"{property.Name} 必须为字符串或字符串数组。");
            selected[property.Name] = values.Values<string>().Select(value => value!.Trim()).ToHashSet(StringComparer.Ordinal);
        }
        await _gate.WaitAsync(_ct).ConfigureAwait(false);
        try
        {
            if (_icons == null) throw new InvalidOperationException("未配置脚本图标缓存。");
            var workspace = await _store.ReadAsync(ResolveUid(), includeHistory: false).ConfigureAwait(false);
            // 仅检查当前账号库存，不遍历整个字典；损坏、无法解码的缓存也视为缺图。
            var names = workspace.Inventory.Items.Keys.Where(name =>
                (!selected.TryGetValue("names", out var namesFilter) || namesFilter.Contains(name)) &&
                (!selected.TryGetValue("categories", out var categories) || categories.Contains(workspace.Definitions.GetValueOrDefault(name, ""))) &&
                (!selected.TryGetValue("pages", out var pages) || pages.Contains(workspace.Pages.GetValueOrDefault(name, ""))))
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var missing = new List<string>();
            foreach (var name in names)
            {
                _ct.ThrowIfCancellationRequested();
                if (_icons.Load(name) == null) missing.Add(name);
            }
            return JsonConvert.SerializeObject(missing);
        }
        finally { _gate.Release(); }
    }

    /// <summary>提取可选目标，拒绝无数量的空记录、重复目标和非法字段。</summary>
    private static Dictionary<string, (long Quantity, string? Category, string? Comment)> ParseDemandTargets(JArray input)
    {
        var targets = new Dictionary<string, (long Quantity, string? Category, string? Comment)>(StringComparer.Ordinal);
        foreach (var token in input)
        {
            if (token is not JObject item) throw new ArgumentException("每条上报记录必须是对象。");
            if (!item.ContainsKey("targetQuantity"))
            {
                if (!item.ContainsKey("quantity")) throw new ArgumentException("记录必须包含 quantity 或 targetQuantity。");
                continue;
            }
            if (item["name"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)item["name"]) ||
                item["targetQuantity"]?.Type != JTokenType.Integer ||
                !item.ContainsKey("quantity") && item.Properties().Any(p => p.Name is not ("name" or "targetQuantity" or "category" or "comment")))
                throw new ArgumentException("需求仅支持 name、targetQuantity、可选 category 和 comment；同时报库存时可使用库存字段。");
            foreach (var field in new[] { "category", "comment" })
                if (item[field] is { Type: not (JTokenType.String or JTokenType.Null) })
                    throw new ArgumentException($"{field} 必须是字符串。");
            var name = ((string)item["name"]!).Trim();
            var quantity = item["targetQuantity"]!.Value<decimal>();
            if (name.Length > 128 || quantity < 0 || quantity > 9007199254740991m)
                throw new ArgumentException("名称不能超过 128 字符，targetQuantity 必须是非负安全整数。");
            if (!targets.TryAdd(name, ((long)quantity, (string?)item["category"], (string?)item["comment"])))
                throw new ArgumentException("同批需求名称不能重复。");
        }
        return targets;
    }

    /// <summary>校验工作目录边界及链接，限量读取后交给缓存服务解码、缩放和保存。</summary>
    private void StoreIcon(string name, JToken token, int pixelSize)
    {
        if (_workDirectory == null || _icons == null) throw new InvalidOperationException("未配置脚本图标缓存。");
        if (token.Type != JTokenType.String) throw new ArgumentException("iconPath 必须是脚本目录内的相对路径。");
        var relative = (string)token;
        if (Path.IsPathRooted(relative)) throw new ArgumentException("iconPath 必须是相对路径。");
        var path = ScriptUtils.NormalizePath(_workDirectory, relative);
        // 不允许目录联接或符号链接绕过工作目录边界。
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("iconPath 不能经过符号链接或目录联接。");
            if (string.Equals(current, _workDirectory, StringComparison.OrdinalIgnoreCase)) break;
        }
        using var stream = File.OpenRead(path);
        if (stream.Length > 4 * 1024 * 1024) throw new ArgumentException("图标文件超过 4 MiB。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        _icons.Store(name, bytes, pixelSize);
    }

    /// <summary>按需读取全局需求判断设置的副本，不绑定游戏 UID，也不提供修改入口。</summary>
    public async Task<string> GetDemandSettings()
    {
        using var operation = ScriptHostOperations.Enter();
        _ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(_ct).ConfigureAwait(false);
        try
        {
            _ct.ThrowIfCancellationRequested();
            var settings = await _store.ReadDemandSettingsAsync().ConfigureAwait(false);
            return JsonConvert.SerializeObject(settings, OutputSettings);
        }
        finally { _gate.Release(); }
    }

    /// <summary>读取最后确认数量；不存在返回 null，不累计待核实增量。</summary>
    public async Task<string> GetQuantities(string json)
    {
        using var operation = ScriptHostOperations.Enter();
        _ct.ThrowIfCancellationRequested();
        var names = ParseNames(json);
        await _gate.WaitAsync(_ct).ConfigureAwait(false);
        try
        {
            _ct.ThrowIfCancellationRequested();
            var workspace = await _store.ReadAsync(ResolveUid(), includeHistory: false).ConfigureAwait(false);
            var result = names.ToDictionary(name => name,
                name => workspace.Inventory.Items.GetValueOrDefault(name)?.FirstOrDefault()?.Quantity, StringComparer.Ordinal);
            // 物品名称是字典键，必须原样输出。
            return JsonConvert.SerializeObject(result);
        }
        finally { _gate.Release(); }
    }

    /// <summary>返回当前确认库存的采集或确认时间（UTC ISO 8601）；失败、增量和目标上报不刷新该时间。</summary>
    public async Task<string> GetReportDates(string json)
    {
        using var operation = ScriptHostOperations.Enter();
        _ct.ThrowIfCancellationRequested();
        var names = ParseNames(json);
        await _gate.WaitAsync(_ct).ConfigureAwait(false);
        try
        {
            _ct.ThrowIfCancellationRequested();
            var workspace = await _store.ReadAsync(ResolveUid(), includeHistory: false).ConfigureAwait(false);
            var result = names.ToDictionary(name => name, name => workspace.Inventory.Items.GetValueOrDefault(name)?
                .FirstOrDefault()?.RecordedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), StringComparer.Ordinal);
            return JsonConvert.SerializeObject(result);
        }
        finally { _gate.Release(); }
    }

    /// <summary>返回当前账号未归档的待核实和错误物品名称；同一物品可同时出现在两组。</summary>
    public async Task<string> GetIssueNames()
    {
        using var operation = ScriptHostOperations.Enter();
        _ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(_ct).ConfigureAwait(false);
        try
        {
            _ct.ThrowIfCancellationRequested();
            var workspace = await _store.ReadAsync(ResolveUid(), includeHistory: false).ConfigureAwait(false);
            var events = workspace.Inventory.Events.Where(item => !item.Archived && !item.ItemDeleted).ToArray();
            var pending = events.Where(item => item.State == "Pending").Select(item => item.Name)
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var errors = workspace.Inventory.Errors.Keys.Concat(events.Where(item => item.State == "Error").Select(item => item.Name))
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            return JsonConvert.SerializeObject(new { pending, errors });
        }
        finally { _gate.Release(); }
    }

    /// <summary>统一名称查询的校验、去空白与去重规则。</summary>
    private static string[] ParseNames(string json)
    {
        var input = Parse(json);
        var values = input is JArray array ? array.ToArray() : new[] { input };
        if (values.Any(value => value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)value)))
            throw new ArgumentException("查询名称必须是非空字符串。");
        return values.Select(value => ((string)value!).Trim()).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>只有得到有效 UID 后才绑定会话，失败不能创建错误账号文件。</summary>
    private string ResolveUid()
    {
        var uid = _uid ?? _readUid();
        if (uid == null || !InventoryStore.IsValidUid(uid)) throw new InvalidOperationException("无法确定当前游戏 UID，库存操作未执行。");
        return _uid = uid!;
    }

    /// <summary>保留原始时间文本，由库存规则判断时区和业务时间。</summary>
    private static JToken Parse(string json) => JsonConvert.DeserializeObject<JToken>(json,
        new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })
        ?? throw new ArgumentException("库存参数不能为空。");

    /// <summary>拒绝未知字段和错误类型，避免拼错类型或漏填数量被静默忽略。</summary>
    private static (InventoryReport Report, bool Delta) ParseRecord(JToken token)
    {
        if (token is not JObject item) throw new ArgumentException("每条库存记录必须是对象。");
        var allowed = new[] { "name", "quantity", "type", "recordedAt", "category", "comment", "expirations" };
        if (item.Properties().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal)))
            throw new ArgumentException("库存记录包含不支持的字段。");
        var type = item["type"];
        if (type != null && (type.Type != JTokenType.String || (string?)type is not ("snapshot" or "delta")))
            throw new ArgumentException("库存记录 type 仅支持 snapshot 或 delta。");
        if (item["name"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)item["name"]) ||
            item["quantity"]?.Type != JTokenType.Integer)
            throw new ArgumentException("库存记录必须包含非空 name 和整数 quantity。");
        // JS Number 超出安全整数范围会丢失精度，进入库存模型前拒绝。
        var quantity = item["quantity"]!.Value<decimal>();
        if (quantity < -9007199254740991m || quantity > 9007199254740991m)
            throw new ArgumentException("quantity 超出 JS 安全整数范围。");
        foreach (var field in new[] { "recordedAt", "category", "comment" })
            if (item[field] is { Type: not (JTokenType.String or JTokenType.Null) })
                throw new ArgumentException($"{field} 必须是字符串。");
        var delta = (string?)type == "delta";
        if (delta && (item.ContainsKey("category") || item.ContainsKey("comment")))
            throw new ArgumentException("增量记录不支持 category 和 comment。");
        if (item["expirations"] is { Type: not JTokenType.Null } expirations)
        {
            if (expirations is not JArray groups) throw new ArgumentException("expirations 必须是数组。");
            foreach (var group in groups)
                if (group is not JObject entry || entry.Properties().Any(p => p.Name is not ("quantity" or "expiresAt")) ||
                    entry["quantity"]?.Type != JTokenType.Integer || entry["expiresAt"]?.Type != JTokenType.String)
                    throw new ArgumentException("到期分组仅支持整数 quantity 和字符串 expiresAt。");
        }
        return (item.ToObject<InventoryReport>()!, delta);
    }
}
