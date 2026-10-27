using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>外部字典校验与稳定英文诊断文本。</summary>
public sealed partial class InventoryStore
{
    /// <summary>补充字典允许清空为零条，不将主动清空误判为损坏并恢复旧备份。</summary>
    internal static string NormalizeUserDictionary(string path, string text) =>
        string.Equals(Path.GetFileName(path), "item-overrides.json", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(text)
            ? "{}" : text;

    /// <summary>合法时间按 UTC 统一指纹，非法原文保持不变以便区分输入。</summary>
    private static string? NormalizeReportTime(string? value) => value != null &&
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)
        ? time.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : value;

    /// <summary>编辑过的字典必须满足结构约束；保留原文件并指出错误字段。</summary>
    internal static void ValidateDictionaryFile(string path, string text)
    {
        var file = Path.GetFileName(path);
        if (file is not ("sources.json" or "item-overrides.json")) return;
        using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
        var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException($"字典根对象后存在多余内容：{path}");
        void Require(bool valid, string field)
        {
            if (!valid) throw new InvalidDataException($"字典字段无效：{path}\n{field}");
        }
        foreach (var property in root.Properties())
        {
            var key = property.Name;
            Require(!string.IsNullOrWhiteSpace(key) && key == key.Trim() && key.Length <= (file == "sources.json" ? 1024 : 128), key);
            Require(property.Value is JObject, key);
            var item = (JObject)property.Value;
            if (file == "item-overrides.json")
            {
                Require(item["Category"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)item["Category"]) && ((string)item["Category"]!).Length <= 128, key + ".Category");
                continue;
            }
            Require(item["Reference"]?.Type == JTokenType.String && (string?)item["Reference"] == key, key + ".Reference");
            Require(item["DisplayName"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)item["DisplayName"]) && ((string)item["DisplayName"]!).Length <= 1024, key + ".DisplayName");
            // 老版本省略 Attempts 时使用模型默认空列表，显式 null 则拒绝。
            if (item["Attempts"] == null) continue;
            Require(item["Attempts"] is JArray, key + ".Attempts");
            foreach (var attempt in (JArray)item["Attempts"]!)
            {
                Require(attempt is JObject, key + ".Attempts[]");
                Require(attempt["Id"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)attempt["Id"]), key + ".Attempts[].Id");
                Require(attempt["Uid"]?.Type == JTokenType.String && System.Text.RegularExpressions.Regex.IsMatch((string)attempt["Uid"]!, "^[0-9]{1,20}$"), key + ".Attempts[].Uid");
                Require(attempt["CreatedAt"]?.Type == JTokenType.String && DateTimeOffset.TryParse((string?)attempt["CreatedAt"], out _), key + ".Attempts[].CreatedAt");
                Require(attempt["Failed"]?.Type == JTokenType.Boolean, key + ".Attempts[].Failed");
                if (attempt["Errors"] != null) Require(attempt["Errors"] is JArray errors && errors.All(error => error.Type == JTokenType.String), key + ".Attempts[].Errors");
            }
        }
    }

    /// <summary>仅翻译本模块固定说明，第三方原始输入不改写。</summary>
    private static string TraceMessage(string message) => message switch
    {
        "当前增减仅为推算，待后续核实。" => "Inventory delta is an estimate pending verification.",
        "核实记录缺少可信扫描时间，无法确认当前库存变动。" => "VerifiedAt is required to verify pending inventory deltas.",
        "核实扫描时间无效，待后续核实。" => "VerifiedAt is invalid; verification remains pending.",
        "核实记录已过期，无法确认当前库存变动。" => "VerifiedAt precedes pending deltas; verification rejected.",
        "扫描时间晚于库存记录时间，无法完成核实。" => "VerifiedAt is later than RecordedAt; verification rejected.",
        "增量必须携带时间戳。" => "RecordedAt is required for a delta.",
        "增量物品不在字典中。" => "Name is not present in the item dictionary.",
        "没有确认库存基准，本次增量不可靠，已拒绝。" => "No confirmed Baseline exists; delta rejected.",
        "增量早于当前确认库存，已拒绝。" => "Delta RecordedAt precedes the confirmed inventory record.",
        "增量推算超出有效库存范围，已拒绝。" => "Estimated Quantity is outside the valid inventory range.",
        "同一批次中存在互相冲突的重复记录。" => "Conflicting records for the same Name exist in one batch.",
        // 兼容更名前已保存的事件说明。
        "名称不在物品字典中，请手动修订名称或确认补充用户字典。" or
        "名称不在物品字典中，请手动修订名称或确认添加到补充字典。" => "Name is not present in the item dictionary; manual correction or confirmation is required.",
        "传入记录早于当前记录，或同一时间的数量不同。请选择接受或舍弃，也可右键手动修改数量。" or
        "传入记录早于当前记录，或同一时间的数量或到期信息不同。请选择接受或舍弃，也可右键手动修改数量。" => "Incoming RecordedAt is older, or Quantity/Expirations differ at the same RecordedAt; accept, discard, or correct manually.",
        "数量不能为负数，请右键手动修改数量。" => "Quantity must be non-negative; manual correction is required.",
        "时间无法解析或缺少时区，请手动修改后重新上报。" => "RecordedAt is invalid or has no time zone.",
        "记录时间超过当前时间五分钟，请核对来源时钟，或右键手动修改数量以重新确认。" => "RecordedAt is more than five minutes in the future; check the source clock.",
        "该材料曾有增减信息，已完成核实。" => "Pending inventory deltas have been verified.",
        "该材料曾有增减信息，已完成核实。人工确认。" => "Pending inventory deltas have been verified manually.",
        "用户标记上次扫描有错误。" => "The user reported an error in the previous scan.",
        "用户已批量归档。" => "The user archived completed events in bulk.",
        "用户已确认。" => "The user acknowledged this event.",
        "用户删除物品记录。" => "The user deleted the item record.",
        "用户接受冲突记录。" => "The user accepted the conflicting record.",
        "用户舍弃异常上报。" => "The user discarded the rejected report.",
        "用户已手动修订库存。" => "The user corrected inventory manually.",
        _ => "See the original Message supplied by the source."
    };
}
