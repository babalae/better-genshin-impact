using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>物品库存阈值的提醒配置；不执行代码，不影响红灯校验。</summary>
public sealed class InventoryWarningRule
{
    /// <summary>库存阈值规则；每条规则独立启用，空列表表示不启用提醒。</summary>
    public List<InventoryThresholdRule> InventoryRules { get; set; } = [];

}

public sealed partial class InventoryStore
{
    /// <summary>最近一次规则读取错误，反馈给页面，不阻断正常库存写入。</summary>
    private string? _thresholdRuleError;

    /// <summary>先验证全部规则；配置损坏时保留原提醒与文件，库存仍正常保存。</summary>
    private bool RefreshThresholdWarnings(InventoryDocument data)
    {
        // 过期检查是内置提醒；规则禁用或损坏时也必须正常执行。
        var changed = RefreshExpirationChecks(data);
        InventoryThresholdRule[] rules;
        try
        {
            rules = ReadWarningRule().InventoryRules.Where(item => item.Enabled).ToArray();
            _thresholdRuleError = null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _thresholdRuleError = ex.Message;
            return changed;
        }
        return EvaluateThresholdWarnings(data, rules) || changed;
    }
    /// <summary>默认提供多种独立规则示例，全部禁用；同一物品的多条规则可同时满足。</summary>
    private static List<InventoryThresholdRule> CreateInventoryRuleExamples() =>
    [
        new() { Id = "resin-expiring", Name = "须臾树脂", Kind = "ExpiresWithinDays", Threshold = 1,
            Message = "须臾树脂存在不足 1 天到期或已过期的批次。" }
    ];

    /// <summary>首次生成多规则示例；配置覆写与订阅留给后续统一机制，本入口仅创建缺失文件。</summary>
    public Task<string> GetWarningRulesPathAsync() => Locked(() =>
    {
        var path = Path.Combine(_root, "warning-rules.json");
        if (!File.Exists(path)) Write(path, new InventoryWarningRule { InventoryRules = CreateInventoryRuleExamples() });
        return path;
    });

    /// <summary>每批上报读取一次；无文件使用默认规则，非法配置明确报错且不覆盖原文。</summary>
    private InventoryWarningRule ReadWarningRule()
    {
        var path = Path.Combine(_root, "warning-rules.json");
        if (!File.Exists(path)) return new InventoryWarningRule();
        try
        {
            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path)));
            var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new InvalidDataException("提醒规则 JSON 根对象后有多余内容。");
            // 兼容旧配置，仅忽略已移除的来源提醒字段；其他未知字段仍报错。
            foreach (var name in new[] { "Enabled", "Source", "WindowSize", "ErrorThreshold", "Message" }) json.Remove(name);
            var rule = json.ToObject<InventoryWarningRule>(JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }))!;
            if (rule.InventoryRules == null || rule.InventoryRules.Count > 100)
                throw new InvalidDataException("InventoryRules 必须为数组，不能为 null，最多 100 条，允许空数组 []。");
            // 逐项定位规则错误，避免将整份约束交给用户猜测。
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < rule.InventoryRules.Count; index++)
            {
                var item = rule.InventoryRules[index];
                var location = $"InventoryRules 第 {index + 1} 条";
                if (item == null) throw new InvalidDataException($"{location} 不能为 null。");
                if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 128)
                    throw new InvalidDataException($"{location}：Id 不能为空且不能超过 128 字符。");
                if (!ids.Add(item.Id)) throw new InvalidDataException($"{location}：Id“{item.Id}”重复。");
                if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 128)
                    throw new InvalidDataException($"{location}（{item.Id}）：Name 须为 1～128 字符的物品名称。");
                if (item.Kind is not ("QuantityBelow" or "ExpiresWithinDays"))
                    throw new InvalidDataException($"{location}（{item.Id}）：Kind 只能为 QuantityBelow（库存不足）或 ExpiresWithinDays（临期）。");
                if (item.Threshold <= 0 || item.Threshold > long.MaxValue ||
                    item.Kind == "QuantityBelow" && decimal.Truncate(item.Threshold) != item.Threshold)
                    throw new InvalidDataException($"{location}（{item.Id}）：Threshold 须大于 0 且不超过 9223372036854775807；库存阈值必须为整数，临期阈值以天为单位。");
                if (item.Message == null || item.Message.Length > 200)
                    throw new InvalidDataException($"{location}（{item.Id}）：Message 不能为 null 或超过 200 字符；使用空文本可自动生成提醒。");
            }
            return rule;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"提醒规则未能加载；库存仍可正常读取和上报。文件：{path}；原因：{ex.Message}", ex);
        }
    }

    /// <summary>仅在规则状态改变时更新事件；未知不作为零，归档不终止当前触发轮次。</summary>
    private bool EvaluateThresholdWarnings(InventoryDocument data, InventoryThresholdRule[] rules)
    {
        var changed = false;
        void End(string id)
        {
            if (!data.ThresholdStates.Remove(id, out var state)) return;
            var previous = data.Events.FirstOrDefault(item => item.Id == state.EventId);
            if (previous != null && !previous.Archived) Transition(previous, "Completed", "规则条件已解除或规则已停用。", true);
            changed = true;
        }
        foreach (var id in data.ThresholdStates.Keys.ToArray())
            if (!rules.Any(rule => rule.Id == id)) End(id);
        var now = DateTimeOffset.UtcNow;
        foreach (var rule in rules)
        {
            var signature = JsonConvert.SerializeObject(rule);
            if (data.ThresholdStates.TryGetValue(rule.Id, out var old) && old.Signature != signature) End(rule.Id);
            var record = data.Items.TryGetValue(rule.Name, out var records) ? records.FirstOrDefault() : null;
            if (record == null) continue;
            // 未提供到期信息时保留原轮次，不把“未知”误认为条件解除。
            if (rule.Kind == "ExpiresWithinDays" && record.Expirations == null && record.Quantity > 0) continue;
            var matched = rule.Kind == "QuantityBelow" ? record.Quantity < rule.Threshold :
                record.Expirations?.Any(item => (decimal)(item.Time - now).TotalDays < rule.Threshold) == true;
            if (!matched) { End(rule.Id); continue; }
            if (data.ThresholdStates.TryGetValue(rule.Id, out var state))
            {
                var active = data.Events.FirstOrDefault(item => item.Id == state.EventId && !item.Archived);
                if (active != null && (active.Quantity != record.Quantity || active.Source != record.Source))
                {
                    active.Quantity = record.Quantity; active.Source = record.Source;
                    active.LastReceivedAt = now;
                    changed = true;
                }
                continue;
            }
            var message = string.IsNullOrWhiteSpace(rule.Message)
                ? rule.Kind == "QuantityBelow" ? $"{rule.Name}的确认库存低于 {rule.Threshold}。" : $"{rule.Name}存在不足 {rule.Threshold} 天到期或已过期的批次。"
                : rule.Message;
            var warning = AddEvent(data, rule.Name, record.Source, "ThresholdWarning", "Warning", record.Quantity, now, message);
            warning.History.Add("Rule=" + signature);
            data.ThresholdStates[rule.Id] = new InventoryThresholdState { Name = rule.Name, Signature = signature, EventId = warning.Id };
            changed = true;
        }
        return changed;
    }
}
