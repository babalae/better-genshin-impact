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
            var item = (JObject)property.Value;
            if (file == "item-overrides.json")
            {
                Require(item["Category"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)item["Category"]) && ((string)item["Category"]!).Length <= 128, key + ".Category", "分类须为 1～128 字符的非空文本");
                continue;
            }
            Require(item["Reference"]?.Type == JTokenType.String && (string?)item["Reference"] == key, key + ".Reference", "来源标识须为文本并与条目键完全一致");
            Require(item["DisplayName"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)item["DisplayName"]) && ((string)item["DisplayName"]!).Length <= 1024, key + ".DisplayName", "显示名称须为 1～1024 字符的非空文本");
            // 老版本省略 Attempts 时使用模型默认空列表，显式 null 则拒绝。
            if (item["Attempts"] == null) continue;
            Require(item["Attempts"] is JArray, key + ".Attempts", "旧版上报记录须为数组，不能为 null");
            foreach (var attempt in (JArray)item["Attempts"]!)
            {
                Require(attempt is JObject, attempt.Path, "旧版上报记录必须为对象");
                Require(attempt["Id"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)attempt["Id"]), attempt.Path + ".Id", "记录标识必须为非空文本");
                Require(attempt["Uid"]?.Type == JTokenType.String && System.Text.RegularExpressions.Regex.IsMatch((string)attempt["Uid"]!, "^[0-9]{1,20}$"), attempt.Path + ".Uid", "旧版账号标识须为 1～20 位数字文本");
                Require(attempt["CreatedAt"]?.Type == JTokenType.String && DateTimeOffset.TryParse((string?)attempt["CreatedAt"], out _), attempt.Path + ".CreatedAt", "创建时间须为可解析的时间文本");
                Require(attempt["Failed"]?.Type == JTokenType.Boolean, attempt.Path + ".Failed", "失败标记只能为 true 或 false");
                if (attempt["Errors"] != null) Require(attempt["Errors"] is JArray errors && errors.All(error => error.Type == JTokenType.String), attempt.Path + ".Errors", "错误说明须为文本数组");
            }
        }
    }

    /// <summary>仅翻译本模块固定说明，第三方原始输入不改写。</summary>
    private static string TraceMessage(string message) => message switch
    {
        "本次上报缺少记录时间，未更新库存。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。" => "RecordedAt is required; inventory was not updated.",
        "本次上报的记录时间早于待核实的库存变动，不能确认变动后的数量。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。" => "RecordedAt precedes pending deltas; verification rejected.",
        "当前增减仅为推算，待后续核实。" => "Inventory delta is an estimate pending verification.",
        "核实记录缺少可信扫描时间，无法确认当前库存变动。" or
        "本次上报缺少扫描时间，不能核实库存变动。请重新扫描，或查看游戏后手动填写当前实际库存总数。" or
        "本次上报缺少核实时间，不能核实库存变动。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。" => "VerifiedAt is required to verify pending inventory deltas.",
        "核实扫描时间无效，待后续核实。" or
        "本次扫描时间无法用于核实，请检查时间格式、时区及来源时钟。也可查看游戏后手动填写当前实际库存总数。" or
        "本次上报的核实时间无效，请检查时间格式、时区及来源时钟。也可查看游戏后手动填写当前实际库存总数。" => "VerifiedAt is invalid; verification remains pending.",
        "核实记录已过期，无法确认当前库存变动。" or
        "本次扫描早于待核实的库存变动，不能确认变动后的数量。请重新扫描，或查看游戏后手动填写当前实际库存总数。" or
        "本次上报的核实时间早于待核实的库存变动，不能确认变动后的数量。请重新采集并上报，或查看游戏后手动填写当前实际库存总数。" => "VerifiedAt precedes pending deltas; verification rejected.",
        "扫描时间晚于库存记录时间，无法完成核实。" or
        "上报的扫描时间晚于库存记录时间，两者顺序不一致。请修正来源的时间字段后重新上报，或查看游戏后手动填写当前实际库存总数。" or
        "上报的核实时间晚于库存记录时间，两者顺序不一致。请修正来源的时间字段后重新上报，或查看游戏后手动填写当前实际库存总数。" => "VerifiedAt is later than RecordedAt; verification rejected.",
        "增量必须携带时间戳。" or
        "库存变动缺少发生时间，本次增减未纳入推算。请来源补充带时区的发生时间后重新上报。" => "RecordedAt is required for a delta.",
        "增量物品不在字典中。" or
        "库存变动的物品名称为空、超过 128 字符或未收录，本次增减未纳入推算。请修正名称或补充物品字典后重新上报。" => "Name is not present in the item dictionary.",
        "没有确认库存基准，本次增量不可靠，已拒绝。" or
        "该物品尚无已确认库存，本次增减未纳入推算。请先扫描库存，或查看游戏后手动填写当前实际库存总数。" or
        "该物品尚无已确认库存，本次增减未纳入推算。请先上报当前库存，或查看游戏后手动填写当前实际库存总数。" => "No confirmed Baseline exists; delta rejected.",
        "增量早于当前确认库存，已拒绝。" or
        "本次变动早于当前确认库存，可能已包含在该库存中，因此未纳入推算。请核对发生时间，勿重复累计；如数量存疑，请查看游戏后手动填写当前实际库存总数。" => "Delta RecordedAt precedes the confirmed inventory record.",
        "增量推算超出有效库存范围，已拒绝。" or
        "按发生时间累计变动后，数量超出有效库存范围（小于 0 或超过 64 位整数上限），本次增减未纳入推算。请核对变动记录，或查看游戏后手动填写当前实际库存总数。" => "Estimated Quantity is outside the valid inventory range.",
        "同一批次中存在互相冲突的重复记录。" or
        "本次上报中，同一物品有多条数量、记录时间或到期信息不一致的记录。请来源合并为一条正确记录后重新上报，或查看游戏后手动填写当前实际库存总数。" => "Conflicting records for the same Name exist in one batch.",
        // 兼容更名前已保存的事件说明。
        "名称不在物品字典中，请手动修订名称或确认补充用户字典。" or
        "名称不在物品字典中，请手动修订名称或确认添加到补充字典。" or
        "物品名称未收录，本次记录未采纳。请改为标准名称，或确认添加到补充字典，再填写游戏中当前实际库存总数。" => "Name is not present in the item dictionary; manual correction or confirmation is required.",
        "传入记录早于当前记录，或同一时间的数量不同。请选择接受或舍弃，也可右键手动修改数量。" or
        "传入记录早于当前记录，或同一时间的数量或到期信息不同。请选择接受或舍弃，也可右键手动修改数量。" or
        "本次上报比当前库存记录更早，或同一时间的数量、到期信息不一致，未覆盖当前库存。可舍弃本次异常上报；也可查看游戏后手动填写当前实际库存总数；保存后将归档该物品已有异常。" => "Incoming RecordedAt is older, or Quantity/Expirations differ at the same RecordedAt; accept, discard, or correct manually.",
        "数量不能为负数，请右键手动修改数量。" or
        "上报的库存数量为负数，本次记录未采纳。请重新采集，或查看游戏后手动填写当前实际库存总数（不是增减数量）。" => "Quantity must be non-negative; manual correction is required.",
        "时间无法解析或缺少时区，请手动修改后重新上报。" or
        "上报的记录时间无法解析或未注明时区。请修正来源或导入文件中的时间后重新上报；也可查看游戏后手动填写当前实际库存总数。" => "RecordedAt is invalid or has no time zone.",
        "记录时间超过当前时间五分钟，请核对来源时钟，或右键手动修改数量以重新确认。" or
        "上报的记录时间比本机时间晚超过五分钟。请核对来源和本机时钟后重新上报，或查看游戏后手动填写当前实际库存总数。" => "RecordedAt is more than five minutes in the future; check the source clock.",
        "该材料曾有增减信息，已完成核实。" => "Pending inventory deltas have been verified.",
        "该材料曾有增减信息，已完成核实。人工确认。" => "Pending inventory deltas have been verified manually.",
        "用户标记上次扫描有错误。" or
        "用户标记上次上报有错误。" => "The user reported an error in the previous report.",
        "用户已批量归档。" => "The user archived completed events in bulk.",
        "用户已确认。" => "The user acknowledged this event.",
        "用户删除物品记录。" => "The user deleted the item record.",
        "用户接受冲突记录。" => "The user accepted the conflicting record.",
        "用户舍弃异常上报。" => "The user discarded the rejected report.",
        "用户已手动修订库存。" => "The user corrected inventory manually.",
        _ => "See the original Message supplied by the source."
    };
}
