using System;
using System.Collections.Generic;
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
        if (reader.Read()) throw new InvalidDataException($"字典 JSON 根对象后有多余内容，原文件未改写：{path}");
        // 同时指出字段位置和约束，保留原文件供用户修订。
        void Require(bool valid, string field, string requirement)
        {
            if (!valid) throw new InvalidDataException($"字典字段无效：{path}\n{field}：{requirement}。原文件未改写。");
        }
        foreach (var property in root.Properties())
        {
            var key = property.Name;
            Require(!string.IsNullOrWhiteSpace(key) && key == key.Trim() && key.Length <= (file == "sources.json" ? 1024 : 128), key,
                $"键不能为空或包含首尾空白，最多 {(file == "sources.json" ? 1024 : 128)} 字符");
            Require(property.Value is JObject, key, "条目必须为 JSON 对象，不能为 null");
            // 与 Json.NET 的属性匹配规则一致，并拒绝仅大小写不同的重复字段。
            var fields = ((JObject)property.Value).Properties().ToArray();
            Require(fields.Select(field => field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == fields.Length,
                key, "字段名称不能以不同大小写重复出现");
            var item = fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.OrdinalIgnoreCase);
            if (file == "item-overrides.json")
            {
                Require(item.GetValueOrDefault("Comment") == null || item.GetValueOrDefault("Comment")!.Type == JTokenType.String, key + ".Comment", "备注须为文本");
                Require(item.GetValueOrDefault("Page") == null || item.GetValueOrDefault("Page")!.Type == JTokenType.Null ||
                    item.GetValueOrDefault("Page")!.Type == JTokenType.String && ((string)item.GetValueOrDefault("Page")!).Length <= 128,
                    key + ".Page", "背包页须为最多 128 字符的文本或 null");
                Require(item.GetValueOrDefault("Category")?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)item.GetValueOrDefault("Category")) && ((string)item.GetValueOrDefault("Category")!).Length <= 128, key + ".Category", "分类须为 1～128 字符的非空文本");
                continue;
            }
            Require(item.GetValueOrDefault("Reference")?.Type == JTokenType.String && (string?)item.GetValueOrDefault("Reference") == key, key + ".Reference", "来源标识须为文本并与条目键完全一致");
            Require(item.GetValueOrDefault("DisplayName")?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)item.GetValueOrDefault("DisplayName")) && ((string)item.GetValueOrDefault("DisplayName")!).Length <= 1024, key + ".DisplayName", "显示名称须为 1～1024 字符的非空文本");
        }
    }

    /// <summary>仅翻译当前业务产生的固定说明，第三方原始输入不改写。</summary>
    private static string TraceMessage(string message) => message switch
    {
        "本次上报缺少记录时间，未更新库存。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。" => "RecordedAt is required; inventory was not updated.",
        "本次上报的记录时间早于待核实的库存变动，不能确认变动后的数量。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。" => "RecordedAt precedes pending deltas; verification rejected.",
        "当前增减仅为推算，待后续核实。" => "Inventory delta is an estimate pending verification.",
        "库存变动缺少发生时间，本次增减未纳入推算。请来源补充带时区的发生时间后重新上报。" => "RecordedAt is required for a delta.",
        "库存变动的物品名称为空、超过 128 字符或未收录，本次增减未纳入推算。请修正名称或补充物品字典后重新上报。" => "Name is not present in the item dictionary.",
        "本次变动早于当前确认库存，可能已包含在该库存中，因此未纳入推算。请核对发生时间，勿重复累计；如数量存疑，请查看游戏后手动填写当前实际库存总数。" => "Delta RecordedAt precedes the confirmed inventory record.",
        "按发生时间累计变动后，数量超出有效库存范围（小于 0 或超过 64 位整数上限），本次增减未纳入推算。请核对变动记录，或查看游戏后手动填写当前实际库存总数。" => "Estimated Quantity is outside the valid inventory range.",
        "本次上报中，同一物品有多条数量、记录时间或到期信息不一致的记录。请来源合并为一条正确记录后重新上报，或查看游戏后手动填写当前实际库存总数。" => "Conflicting records for the same Name exist in one batch.",
        "本次上报比当前库存记录更早，或同一时间的数量、到期信息不一致，未覆盖当前库存。可舍弃本次异常上报；也可查看游戏后手动填写当前实际库存总数；保存后将归档该物品已有异常。" => "Incoming RecordedAt is older, or Quantity/Expirations differ at the same RecordedAt; accept, discard, or correct manually.",
        "上报的库存数量为负数，本次记录未采纳。请重新采集，或查看游戏后手动填写当前实际库存总数（不是增减数量）。" => "Quantity must be non-negative; manual correction is required.",
        "上报的记录时间无法解析或未注明时区。请修正来源或导入文件中的时间后重新上报；也可查看游戏后手动填写当前实际库存总数。" => "RecordedAt is invalid or has no time zone.",
        "上报的记录时间比本机时间晚超过五分钟。请核对来源和本机时钟后重新上报，或查看游戏后手动填写当前实际库存总数。" => "RecordedAt is more than five minutes in the future; check the source clock.",
        "该材料曾有增减信息，已完成核实。" => "Pending inventory deltas have been verified.",
        "用户已批量归档。" => "The user archived completed events in bulk.",
        "用户已确认。" => "The user acknowledged this event.",
        "用户删除物品记录。" => "The user deleted the item record.",
        "用户舍弃异常上报。" => "The user discarded the rejected report.",
        _ => message
    };
}
