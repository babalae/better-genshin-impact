using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>交换文件中的需求子表，与库存共用备份文件但保留独立状态。</summary>
public sealed partial class InventoryStore
{
    /// <summary>将记录时的完整需求和所引用的来源加入账号备份。</summary>
    private void AddDemandBackup(JObject package, string uid, InventoryDocument inventory)
    {
        var demands = ReadDemands(uid);
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() }
        });
        package["accounts"]![uid]!["demands"] = JObject.FromObject(demands, serializer);
        var sources = Sources();
        // 固定记录当时的灯色及推算依据；恢复时不把历史估算冒充人工确认。
        var workspace = new InventoryWorkspace { Inventory = inventory, Settings = Settings(),
            Sources = sources, Definitions = Definitions(), Pages = PageDefinitions() };
        package["accounts"]![uid]!["demandStates"] = JObject.FromObject(MaterialDemandPolicy.EvaluateAll(workspace, demands), serializer);
        var origins = (JObject)package["sources"]!;
        foreach (var reference in demands.Items.Values.Select(item => item.Source)
                     .Concat(demands.History.Select(item => item.Demand.Source))
                     .Concat(demands.History.Select(item => item.Source)).Where(source => !string.IsNullOrWhiteSpace(source)).Distinct())
            origins[reference] = new JObject { ["reference"] = reference,
                ["displayName"] = sources.TryGetValue(reference, out var source) ? source.DisplayName : reference };
        // 无库存的需求仍可引用用户字典；随备份保留，避免恢复后失去类型。
        var overrides = Read<Dictionary<string, InventoryItemOverride>>(Path.Combine(_root, "item-overrides.json"), restoreBackup: false);
        foreach (var name in demands.Items.Keys)
            if (overrides.TryGetValue(name, out var definition))
            {
                var entry = new JObject { ["category"] = definition.Category, ["comment"] = definition.Comment };
                if (definition.Page != null) entry["page"] = definition.Page;
                package["itemDefinitions"]![name] = entry;
            }
    }

    /// <summary>导入前验证完整需求文档，禁止缺字段被模型默认值补成人工创建。</summary>
    private static MaterialDemandDocument ValidateDemandBackup(JToken token, JObject sources)
    {
        if (token is not JObject document || document["schemaVersion"]?.Type != JTokenType.Integer ||
            (int)document["schemaVersion"]! != 1 || document["items"] is not JObject items ||
            document["history"] is not JArray history || document["prior"] is not JArray prior || items.Count > MaxDemandItems)
            throw new InvalidDataException("需求子表必须包含有效的 schemaVersion、items、history 和 prior。");
        foreach (var item in items.Properties())
        {
            ValidateDemandName(item.Name);
            ValidateDemandEntry(item.Value, sources);
        }
        foreach (var name in prior)
        {
            if (name.Type != JTokenType.String) throw new InvalidDataException("需求置顶项须为物品名称。");
            ValidateDemandName((string)name!);
        }
        foreach (var entry in history)
        {
            if (entry is not JObject archive || archive["name"]?.Type != JTokenType.String ||
                archive["source"]?.Type != JTokenType.String || archive["sourceDisplayName"]?.Type != JTokenType.String ||
                archive["confirmationMethod"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)archive["confirmationMethod"]))
                throw new InvalidDataException("需求归档条目不完整。");
            ValidateDemandName((string)archive["name"]!);
            ValidateDemandEntry(archive["demand"]!, sources);
            ValidateDemandTime(archive["completedAt"]);
            ValidateDemandTime(archive["archivedAt"]);
            if (archive["craftingReservePercent"] is { Type: not JTokenType.Integer } ||
                (int?)archive["craftingReservePercent"] is < 0 or > 20)
                throw new InvalidDataException("需求归档时间或合成收益预留无效。");
        }
        return document.ToObject<MaterialDemandDocument>()!;
    }

    /// <summary>检查单个目标的必要字段及确认时间，不接受负数目标。</summary>
    private static void ValidateDemandEntry(JToken? token, JObject sources)
    {
        if (token is not JObject demand || demand["targetQuantity"]?.Type != JTokenType.Integer ||
            demand["targetQuantity"]!.Value<long>() < 0 || demand["revision"]?.Type != JTokenType.String ||
            string.IsNullOrWhiteSpace((string?)demand["revision"]) || demand["source"]?.Type != JTokenType.String ||
            sources[(string)demand["source"]!] is not JObject)
            throw new InvalidDataException("需求条目须包含非负整数目标、版本及有效来源。");
        var createdAt = ValidateDemandTime(demand["createdAt"]);
        if (demand["confirmedAt"] is { Type: not JTokenType.Null } confirmed && ValidateDemandTime(confirmed) < createdAt)
            throw new InvalidDataException("需求确认时间不能早于登记时间。");
    }

    /// <summary>只接受带明确时区的完整时间，不以本机时区解释备份。</summary>
    private static DateTimeOffset ValidateDemandTime(JToken? token)
    {
        var text = token?.Type == JTokenType.Date ? token.Value<DateTimeOffset>().ToString("O") : token?.Type == JTokenType.String ? (string?)token : null;
        if (token == null || token.Type is not (JTokenType.String or JTokenType.Date) ||
            string.IsNullOrWhiteSpace(text) || !Regex.IsMatch(text, @"(?:[zZ]|[+-]\d{2}:?\d{2})$") ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) || time == default)
            throw new InvalidDataException("需求时间须为带时区的有效时间。");
        return time;
    }

    /// <summary>需求物品名与库存交换约束一致。</summary>
    private static void ValidateDemandName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 128)
            throw new InvalidDataException("需求物品名称须为 1～128 字符且无首尾空白。");
    }
}
