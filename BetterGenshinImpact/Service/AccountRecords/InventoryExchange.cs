using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>单账号库存交换；导出当前库存，导入另支持经确认的开发测试增量，不交换错误和快照。</summary>
public sealed partial class InventoryStore
{
    /// <summary>读取有限大小的交换文件；保留原始时间字符串，交由库存校验报告时间错误。</summary>
    public static async Task<JObject> ReadExchangeAsync(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("导入文件超过 8 MiB，未载入。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var json = new JsonTextReader(new StringReader(await reader.ReadToEndAsync())) { DateParseHandling = DateParseHandling.None, MaxDepth = 16 };
        var package = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (json.Read()) throw new InvalidDataException("导入文件包含多个根对象或末尾多余内容，未载入。");
        ValidateExchange(package);
        return package;
    }

    /// <summary>导入前完整检查结构，禁止缺字段被当成零数量或当前时间。</summary>
    private static void ValidateExchange(JObject package)
    {
        if ((string?)package["format"] != "BetterGI.AccountRecords" || package["version"]?.Type != JTokenType.Integer || (int)package["version"]! != 1)
            throw new InvalidDataException("文件格式或版本不受支持。仅支持狛荷屋库存 JSON（format=BetterGI.AccountRecords，version=1）。");
        if (package["accounts"] is not JObject accounts || accounts.Count != 1 ||
            package["sources"] is not JObject sources || package["itemDefinitions"] is not JObject definitions)
            throw new InvalidDataException("导入文件必须只包含一个账号，并提供 sources（来源字典）和 itemDefinitions（补充字典）对象；字典无内容时可使用空对象。");
        var account = accounts.Properties().Single();
        // 先检查全部来源，避免导入部分库存后才发现后续来源无法保存。
        foreach (var entry in sources.Properties())
        {
            if (entry.Value is not JObject descriptor || entry.Name != entry.Name.Trim() ||
                (string?)descriptor["reference"] != entry.Name || descriptor["displayName"]?.Type != JTokenType.String)
                throw new InvalidDataException($"来源“{entry.Name}”结构不正确：条目须为对象，reference 须与来源键一致且无首尾空白，displayName 须为文本。");
            _ = ValidateSource(new AccountRecordSource { Reference = entry.Name, DisplayName = (string)descriptor["displayName"]! });
        }
        if (!IsValidUid(account.Name))
            throw new InvalidDataException($"导入 UID“{account.Name}”无效，须为首位非零的 9～10 位数字。");
        if (account.Value is not JObject body || body["inventory"] is not JObject inventory)
            throw new InvalidDataException($"账号 {account.Name} 缺少有效库存对象。账号内容及 inventory 必须为 JSON 对象。");
        if (inventory.Count > 5000)
            throw new InvalidDataException($"账号 {account.Name} 包含 {inventory.Count} 项库存，超过 5000 项上限。");
        foreach (var item in inventory.Properties())
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name != item.Name.Trim() || item.Name.Length > 128 ||
                item.Value is not JArray records || records.Count != 1 || records[0] is not JObject record ||
                record["quantity"]?.Type != JTokenType.Integer || record["recordedAt"]?.Type != JTokenType.String ||
                string.IsNullOrWhiteSpace((string?)record["recordedAt"]) || record["source"]?.Type != JTokenType.String)
                throw new InvalidDataException($"{item.Name}：物品名称须为 1～128 字符且无首尾空白；记录数组必须恰有一条，包含整数 quantity、非空时间文本 recordedAt 和来源文本 source。");
            _ = record["quantity"]!.Value<long>();
            if (record["expirations"] is { Type: not JTokenType.Null } expirations)
            {
                if (expirations is not JArray batches || batches.Count > 100 || batches.Any(batch => batch is not JObject ||
                    batch["quantity"]?.Type != JTokenType.Integer || batch["expiresAt"]?.Type != JTokenType.String))
                    throw new InvalidDataException($"{item.Name}：到期批次 expirations 必须为数组且不超过 100 批，每批须有整数 quantity 和时间文本 expiresAt（时间须注明时区）。");
            }
            // 严格采用单时间标准，不转换或忽略旧双时间字段，包括显式 null。
            if (record.Property("verifiedAt") != null)
                throw new InvalidDataException($"{item.Name}：不支持 verifiedAt 字段，库存记录仅使用 recordedAt。");
            var source = (string)record["source"]!;
            if (source.Length is 0 or > 1024 || source != source.Trim() || sources[source] is not JObject descriptor ||
                (string?)descriptor["reference"] != source || descriptor["displayName"]?.Type != JTokenType.String)
                throw new InvalidDataException($"{item.Name}：source 未对应有效来源。source 须为 1～1024 字符且无首尾空白，并对应 sources 中 reference 一致、displayName 为文本的条目。");
        }
        foreach (var item in definitions.Properties())
            if (inventory[item.Name] == null || item.Value is not JObject definition || definition["category"]?.Type != JTokenType.String ||
                string.IsNullOrWhiteSpace((string?)definition["category"]) || ((string)definition["category"]!).Length > 128)
                throw new InvalidDataException($"补充字典条目“{item.Name}”无效：物品须存在于本文件库存中，且提供 1～128 字符的非空分类 category。");
        if (body["deltas"] != null)
        {
            if (body["deltas"] is not JArray deltas || deltas.Count > 5000) throw new InvalidDataException("导入文件的 deltas 必须为数组且不超过 5000 项。");
            foreach (var delta in deltas)
            {
                if (delta is not JObject || delta["name"]?.Type != JTokenType.String || delta["delta"]?.Type != JTokenType.Integer ||
                    delta["recordedAt"]?.Type != JTokenType.String || delta["source"]?.Type != JTokenType.String ||
                    sources[(string)delta["source"]!] is not JObject descriptor || (string?)descriptor["reference"] != (string?)delta["source"] ||
                    descriptor["displayName"]?.Type != JTokenType.String) throw new InvalidDataException($"变动条目 {delta.Path} 不完整：须包含文本 name、整数 delta、时间文本 recordedAt，以及在 sources 中登记的 source。");
                _ = delta["delta"]!.Value<long>();
                if (delta["expirations"] is { Type: not JTokenType.Null } expirationToken &&
                    (expirationToken is not JArray batches || batches.Count > 100 || batches.Any(batch => batch is not JObject ||
                        batch["quantity"]?.Type != JTokenType.Integer || batch["expiresAt"]?.Type != JTokenType.String)))
                    throw new InvalidDataException($"变动条目 {delta.Path} 的 expirations 须为数组且不超过 100 批，每批须提供整数 quantity 和带时区的时间文本 expiresAt。");
            }
        }
    }

    /// <summary>导出当前项及所引用的来源和用户补充；不暴露本机路径、错误和缓存。</summary>
    public Task<JObject> ExportInventoryAsync(string uid) => Locked(() =>
    {
        var data = Load(uid);
        var sources = Sources();
        var overrides = Read<Dictionary<string, InventoryItemOverride>>(Path.Combine(_root, "item-overrides.json"));
        var inventory = new JObject();
        var origins = new JObject();
        var definitions = new JObject();
        foreach (var item in data.Items.Where(pair => pair.Value.Count > 0))
        {
            var record = item.Value[0];
            inventory[item.Key] = new JArray(new JObject
            {
                ["quantity"] = record.Quantity, ["recordedAt"] = record.RecordedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), ["source"] = record.Source
            });
            if (record.Expirations != null)
                inventory[item.Key]![0]!["expirations"] = new JArray(record.Expirations.Select(batch => new JObject
                { ["quantity"] = batch.Quantity, ["expiresAt"] = batch.ExpiresAt }));
            origins[record.Source] = new JObject { ["reference"] = record.Source,
                ["displayName"] = sources.TryGetValue(record.Source, out var origin) ? origin.DisplayName : record.Source };
            if (overrides.TryGetValue(item.Key, out var definition)) definitions[item.Key] = new JObject { ["category"] = definition.Category };
        }
        return new JObject
        {
            ["format"] = "BetterGI.AccountRecords", ["version"] = 1,
            ["exportedAt"] = DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["sources"] = origins, ["itemDefinitions"] = definitions,
            ["accounts"] = new JObject { [uid] = new JObject { ["inventory"] = inventory } }
        };
    });

    /// <summary>按来源局部合并；每批复用库存的校验、冲突记录和原子保存，不清除未提及的物品。</summary>
    /// <remarks>字典补充和各来源分别提交；后续批次失败不会回滚已保存内容。</remarks>
    public async Task<InventoryReportResult> ImportInventoryAsync(JObject package, string uid, bool includeDefinitions, bool allowDeveloperDeltas = false)
    {
        package = (JObject)package.DeepClone();
        ValidateExchange(package);
        var body = ((JObject)package["accounts"]!).Properties().Single().Value;
        var deltas = body["deltas"] as JArray;
        if (deltas is { Count: > 0 } && !allowDeveloperDeltas) throw new InvalidOperationException("文件包含开发测试用库存增量，尚未获得导入确认。");
        _ = AccountDirectory(uid);
        if (includeDefinitions)
            await Locked(() =>
            {
                var definitions = Definitions();
                var path = Path.Combine(_root, "item-overrides.json");
                var overrides = Read<Dictionary<string, InventoryItemOverride>>(path);
                foreach (var item in ((JObject)package["itemDefinitions"]!).Properties())
                    if (!definitions.TryGetValue(item.Name, out var category) || string.IsNullOrEmpty(category))
                        overrides[item.Name] = new InventoryItemOverride { Category = ((string)item.Value["category"]!).Trim() };
                Write(path, overrides);
                return true;
            });
        var inventory = (JObject)((JObject)package["accounts"]!).Properties().Single().Value["inventory"]!;
        var result = new InventoryReportResult();
        foreach (var group in inventory.Properties().GroupBy(item => (string)item.Value[0]!["source"]!, StringComparer.Ordinal))
        {
            var descriptor = package["sources"]![group.Key]!;
            // 已有本地来源名称优先，外部文件不能改名影响其他账号的展示。
            var source = new AccountRecordSource
            { Reference = group.Key, DisplayName = (string)descriptor["displayName"]! };
            var batch = group.Select(item => new InventoryReport { Name = item.Name,
                Category = (string?)package["itemDefinitions"]?[item.Name]?["category"],
                Expirations = item.Value[0]!["expirations"]?.ToObject<List<InventoryExpiration>>(),
                Quantity = (long)item.Value[0]!["quantity"]!, RecordedAt = (string)item.Value[0]!["recordedAt"]! });
            var imported = await ReportAsync(uid, source, batch, preserveSourceName: true);
            result.Accepted.AddRange(imported.Accepted);
            foreach (var warning in imported.Warnings) result.Warnings[warning.Key] = warning.Value;
            result.DuplicateCount += imported.DuplicateCount;
            result.DuplicateErrorCount += imported.DuplicateErrorCount;
            foreach (var error in imported.Rejected) result.Rejected[error.Key] = error.Value;
        }
        if (inventory.Count == 0) await AddUidAsync(uid);
        if (deltas != null)
        {
            var imported = await ReportDeltasAsync(uid, deltas.Select(item =>
            {
                var reference = (string)item["source"]!;
                return (new AccountRecordSource { Reference = reference, DisplayName = (string)package["sources"]![reference]!["displayName"]! },
                    new InventoryDeltaReport { Name = (string)item["name"]!, Delta = (long)item["delta"]!, RecordedAt = (string)item["recordedAt"]!,
                        Expirations = item["expirations"]?.ToObject<List<InventoryExpiration>>() });
            }));
            result.Accepted.AddRange(imported.Accepted);
            result.DuplicateCount += imported.DuplicateCount;
            result.DuplicateErrorCount += imported.DuplicateErrorCount;
            foreach (var error in imported.Rejected) result.Rejected[error.Key] = error.Value;
        }
        return result;
    }
}
