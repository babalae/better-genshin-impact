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
            throw new InvalidDataException("导入文件必须只包含一个账户，并提供 sources（来源字典）和 itemDefinitions（补充字典）对象；字典无内容时可使用空对象。");
        var account = accounts.Properties().Single();
        if (package["batchId"] != null)
        {
            if (package["batchId"]!.Type != JTokenType.String) throw new InvalidDataException("batchId 必须是文本。");
            _ = BatchIdentifier((string)package["batchId"]!);
        }
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
            throw new InvalidDataException($"UID {account.Name} 缺少有效库存对象。账户内容及 inventory 必须为 JSON 对象。");
        if (inventory.Count > 5000)
            throw new InvalidDataException($"UID {account.Name} 包含 {inventory.Count} 项库存，超过 5000 项上限。");
        if (body["demands"] != null) ValidateDemandBackup(body["demands"]!, sources);
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
        {
            if (item.Value is JObject commentDefinition && commentDefinition["comment"] != null && commentDefinition["comment"]!.Type != JTokenType.String)
                throw new InvalidDataException($"补充字典条目“{item.Name}”的 comment 须为文本。");
            if (item.Value is JObject pageDefinition && pageDefinition["page"] is { Type: not JTokenType.Null } page &&
                (page.Type != JTokenType.String || ((string)page).Length > 128))
                throw new InvalidDataException($"补充字典条目“{item.Name}”的 page 须为最多 128 字符的文本或 null。");
            if ((inventory[item.Name] == null && body["demands"]?["items"]?[item.Name] == null) || item.Value is not JObject definition || definition["category"]?.Type != JTokenType.String ||
                string.IsNullOrWhiteSpace((string?)definition["category"]) || ((string)definition["category"]!).Length > 128)
                throw new InvalidDataException($"补充字典条目“{item.Name}”无效：物品须存在于本文件库存或需求中，且提供 1～128 字符的非空分类 category。");
        }
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
                // 增量同样遵循单时间标准，旧字段即使为 null 也不能静默忽略。
                if (((JObject)delta).Property("verifiedAt") != null)
                    throw new InvalidDataException($"变动条目 {delta.Path} 不支持 verifiedAt 字段，库存变动仅使用 recordedAt。");
                // 在任何库存或字典写入前拒绝空名称，避免增量阶段失败时已保存部分库存。
                if (string.IsNullOrWhiteSpace((string?)delta["name"]))
                    throw new InvalidDataException($"变动条目 {delta.Path} 的物品名称不能为空或仅包含空白字符，文件未导入。");
                if (delta["expirations"] is { Type: not JTokenType.Null } expirationToken &&
                    (expirationToken is not JArray batches || batches.Count > 100 || batches.Any(batch => batch is not JObject ||
                        batch["quantity"]?.Type != JTokenType.Integer || batch["expiresAt"]?.Type != JTokenType.String)))
                    throw new InvalidDataException($"变动条目 {delta.Path} 的 expirations 须为数组且不超过 100 批，每批须提供整数 quantity 和带时区的时间文本 expiresAt。");
            }
        }
    }

    /// <summary>导出当前库存、需求及所引用的来源和用户补充；不暴露本机路径、错误和缓存。</summary>
    public Task<JObject> ExportInventoryAsync(string uid) => Locked(() => CreateInventoryPackage(Load(uid)));

    /// <summary>读取交换记录的可选身份；未传入时按文件内稳定顺序编号。</summary>
    private static string ExchangeRecordId(JToken item, string fallback)
    {
        if (item["recordId"] == null) return fallback;
        if (item["recordId"]!.Type != JTokenType.String) throw new InvalidDataException("recordId 必须是文本。");
        return BatchIdentifier((string)item["recordId"]!);
    }

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
        // 同一导入共享批次 ID；默认编号在按来源分组前分配，跨来源不会重复。
        var importBatchId = BatchIdentifier((string?)package["batchId"]);
        var inventory = (JObject)body["inventory"]!;
        var inventoryIds = inventory.Properties().Select((item, index) => (item.Name,
            Id: ExchangeRecordId(item.Value[0]!, "r" + (index + 1).ToString(CultureInfo.InvariantCulture))))
            .ToDictionary(item => item.Name, item => item.Id, StringComparer.Ordinal);
        var deltaIds = deltas?.Select((item, index) => ExchangeRecordId(item, "d" + (index + 1).ToString(CultureInfo.InvariantCulture))).ToArray() ?? [];
        if (inventoryIds.Values.Concat(deltaIds).Distinct(StringComparer.Ordinal).Count() != inventoryIds.Count + deltaIds.Length)
            throw new InvalidDataException("导入批次中的 recordId 必须唯一。");
        // 在字典和任何来源提交前检查整批身份冲突；保留既有各来源分段提交的行为。
        await Locked(() =>
        {
            var inputs = inventory.Properties().Select(item => BatchInput(inventoryIds[item.Name], item.Name,
                NormalizeSource((string)item.Value[0]!["source"]!), "Report", new InventoryReport
                {
                    Name = item.Name, Quantity = (long)item.Value[0]!["quantity"]!, RecordedAt = (string)item.Value[0]!["recordedAt"]!,
                    Category = (string?)package["itemDefinitions"]?[item.Name]?["category"], Comment = (string?)package["itemDefinitions"]?[item.Name]?["comment"],
                    Expirations = item.Value[0]!["expirations"]?.ToObject<List<InventoryExpiration>>()
                })).ToList();
            if (deltas != null) inputs.AddRange(deltas.Select((item, index) => BatchInput(deltaIds[index], (string)item["name"]!,
                NormalizeSource((string)item["source"]!), "Delta", new InventoryDeltaReport { Name = (string)item["name"]!, Delta = (long)item["delta"]!,
                    RecordedAt = (string)item["recordedAt"]!, Expirations = item["expirations"]?.ToObject<List<InventoryExpiration>>() })));
            _ = BeginBatch(Load(uid), importBatchId, "Import", inputs);
            return true;
        });
        var result = new InventoryReportResult { BatchId = importBatchId };
        var definitionWarnings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (includeDefinitions)
            await Locked(() =>
            {
                var definitions = Definitions();
                var pages = PageDefinitions();
                var path = Path.Combine(_root, "item-overrides.json");
                var overrides = Read<Dictionary<string, InventoryItemOverride>>(path);
                var changed = false;
                foreach (var item in ((JObject)package["itemDefinitions"]!).Properties())
                {
                    overrides.TryGetValue(item.Name, out var previous);
                    var conflicts = new List<string>();
                    var localCategory = definitions.GetValueOrDefault(item.Name);
                    var category = MergeDefinitionField(localCategory, ((string)item.Value["category"]!).Trim(), "分类", conflicts)!;
                    var localPage = pages.GetValueOrDefault(item.Name);
                    // 补充字典显式空 Page 表示用户选择未分类，不当作可自动填充的缺省值。
                    var page = MergeDefinitionField(localPage, (string?)item.Value["page"], "背包页", conflicts, previous?.Page == "");
                    var incomingComment = (string?)item.Value["comment"];
                    var comment = string.IsNullOrWhiteSpace(previous?.Comment) ? incomingComment ?? "" : previous.Comment;
                    // 显式空值是持续覆盖 CSV 的意图，即使当前有效值相同也必须保存。
                    var explicitEmptyPage = (string?)item.Value["page"] == "" && page == "";
                    if (category != localCategory || page != localPage || comment != (previous?.Comment ?? "") || explicitEmptyPage && previous?.Page != "")
                    {
                        overrides[item.Name] = new InventoryItemOverride
                        {
                            Category = category,
                            Page = explicitEmptyPage ? "" : page != localPage ? page : previous?.Page,
                            Comment = comment
                        };
                        changed = true;
                    }
                    if (conflicts.Count > 0) definitionWarnings[item.Name] = string.Join(Environment.NewLine, conflicts);
                }
                if (changed) Write(path, overrides);
                return true;
            });
        foreach (var group in inventory.Properties().GroupBy(item => (string)item.Value[0]!["source"]!, StringComparer.Ordinal))
        {
            var descriptor = package["sources"]![group.Key]!;
            // 已有本地来源名称优先，外部文件不能改名影响其他账号的展示。
            var source = new AccountRecordSource
            { Reference = group.Key, DisplayName = (string)descriptor["displayName"]! };
            var batch = group.Select(item => new InventoryReport { RecordId = inventoryIds[item.Name], Name = item.Name,
                Category = (string?)package["itemDefinitions"]?[item.Name]?["category"],
                Comment = (string?)package["itemDefinitions"]?[item.Name]?["comment"],
                Expirations = item.Value[0]!["expirations"]?.ToObject<List<InventoryExpiration>>(),
                Quantity = (long)item.Value[0]!["quantity"]!, RecordedAt = (string)item.Value[0]!["recordedAt"]! });
            var imported = await ReportAsync(uid, source, batch, preserveSourceName: true, supplementDefinitions: includeDefinitions, batchId: importBatchId);
            result.Accepted.AddRange(imported.Accepted);
            result.Records.AddRange(imported.Records);
            foreach (var warning in imported.Warnings) result.Warnings[warning.Key] = warning.Value;
            result.DuplicateCount += imported.DuplicateCount;
            result.DuplicateErrorCount += imported.DuplicateErrorCount;
            foreach (var error in imported.Rejected) result.Rejected[error.Key] = error.Value;
        }
        if (inventory.Count == 0) await AddUidAsync(uid);
        if (deltas != null)
        {
            var imported = await ReportDeltasAsync(uid, deltas.Select((item, index) =>
            {
                var reference = (string)item["source"]!;
                return (new AccountRecordSource { Reference = reference, DisplayName = (string)package["sources"]![reference]!["displayName"]! },
                    new InventoryDeltaReport { RecordId = deltaIds[index], Name = (string)item["name"]!, Delta = (long)item["delta"]!, RecordedAt = (string)item["recordedAt"]!,
                        Expirations = item["expirations"]?.ToObject<List<InventoryExpiration>>() });
            }), importBatchId);
            result.Accepted.AddRange(imported.Accepted);
            result.Records.AddRange(imported.Records);
            result.DuplicateCount += imported.DuplicateCount;
            result.DuplicateErrorCount += imported.DuplicateErrorCount;
            foreach (var error in imported.Rejected) result.Rejected[error.Key] = error.Value;
        }
        // 分类检查和字段合并的提醒都保留，背包页冲突不能被后续分类提醒覆盖。
        // 需求子表作为完整备份恢复；普通库存文件没有该节点时保留现有需求。
        if (body["demands"] != null)
            await Locked(() =>
            {
                var demands = ValidateDemandBackup(body["demands"]!, (JObject)package["sources"]!);
                var sources = Sources();
                foreach (var source in ((JObject)package["sources"]!).Properties())
                    sources.TryAdd(source.Name, source.Value.ToObject<AccountRecordSource>()!);
                SaveSources();
                Write(Path.Combine(AccountDirectory(uid), "demands.json"), demands);
                return true;
            });
        foreach (var warning in definitionWarnings)
            result.Warnings[warning.Key] = result.Warnings.TryGetValue(warning.Key, out var existing)
                ? existing + Environment.NewLine + warning.Value : warning.Value;
        return result;
    }
}
