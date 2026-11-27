using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>日期快照的只读查询及可导入格式转换，不恢复或修改当前库存。</summary>
public sealed partial class InventoryStore
{
    /// <summary>只枚举日期，不预先解析快照。</summary>
    public Task<string[]> GetSnapshotDatesAsync(string uid) => Locked(() =>
    {
        var directory = Path.Combine(AccountDirectory(uid), "snapshots");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(IsSnapshotDate).OrderByDescending(day => day, StringComparer.Ordinal).ToArray() : [];
    }, initialize: false);

    /// <summary>限制日期为固定格式，禁止外部传入路径。</summary>
    private static bool IsSnapshotDate(string date) => DateTime.TryParseExact(date, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>读取单日导入格式；旧快照不迁移，历史视图仅展示确认数量。</summary>
    public Task<InventoryWorkspace> ReadSnapshotAsync(string uid, string date) => Locked(() =>
    {
        if (!IsSnapshotDate(date)) throw new ArgumentException("快照日期格式无效。");
        var path = Path.Combine(AccountDirectory(uid), "snapshots", date + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException("快照不存在或已被清理。", path);
        var json = Read<JObject>(path, restoreBackup: false);
        var workspace = new InventoryWorkspace
        {
            Definitions = Definitions(false), Pages = PageDefinitions(false), Sources = Sources(false), Settings = Settings(false),
            Inventory = new InventoryDocument { Uid = uid }
        };
        ValidateExchange(json);
        // 账号目录确定归属；UID 改名后仍保留快照当时的账号标识，不改写历史文件。
        var account = (JObject)((JObject)json["accounts"]!).Properties().Single().Value;
        workspace.Inventory.Items = account["inventory"]!.ToObject<Dictionary<string, List<InventoryRecord>>>()!;
        workspace.Sources = json["sources"]!.ToObject<Dictionary<string, AccountRecordSource>>()!;
        foreach (var definition in ((JObject)json["itemDefinitions"]!).Properties())
        {
            workspace.Definitions[definition.Name] = (string)definition.Value["category"]!;
            if (definition.Value["page"]?.Type == JTokenType.String)
                workspace.Pages[definition.Name] = (string)definition.Value["page"]!;
        }
        return workspace;
    }, initialize: false);

    /// <summary>导出与日期快照共用可导入格式，保存确认库存、原时间、来源、需求及补充字典。</summary>
    private JObject CreateInventoryPackage(InventoryDocument data)
    {
        var sources = Sources();
        var overrides = Read<Dictionary<string, InventoryItemOverride>>(Path.Combine(_root, "item-overrides.json"), restoreBackup: false);
        var inventory = new JObject();
        var origins = new JObject();
        var definitions = new JObject();
        foreach (var (name, records) in data.Items.Where(pair => pair.Value.Count > 0))
        {
            var record = records[0];
            var item = new JObject { ["quantity"] = record.Quantity,
                ["recordedAt"] = record.RecordedAt.ToString("O", CultureInfo.InvariantCulture), ["source"] = record.Source };
            if (record.Expirations != null) item["expirations"] = new JArray(record.Expirations.Select(batch =>
                new JObject { ["quantity"] = batch.Quantity, ["expiresAt"] = batch.ExpiresAt }));
            inventory[name] = new JArray(item);
            origins[record.Source] = new JObject { ["reference"] = record.Source,
                ["displayName"] = sources.TryGetValue(record.Source, out var source) ? source.DisplayName : record.Source };
            if (overrides.TryGetValue(name, out var definition) && !string.IsNullOrWhiteSpace(definition.Category))
            {
                var entry = new JObject { ["category"] = definition.Category, ["comment"] = definition.Comment };
                if (definition.Page != null) entry["page"] = definition.Page;
                definitions[name] = entry;
            }
        }
        var package = new JObject { ["format"] = "BetterGI.AccountRecords", ["version"] = 1,
            ["exportedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["sources"] = origins, ["itemDefinitions"] = definitions,
            ["accounts"] = new JObject { [data.Uid] = new JObject { ["inventory"] = inventory } } };
        AddDemandBackup(package, data.Uid, data);
        return package;
    }
}
