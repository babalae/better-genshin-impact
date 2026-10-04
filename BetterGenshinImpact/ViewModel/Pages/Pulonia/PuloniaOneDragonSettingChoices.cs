using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙设置卡片的展示选项；只修饰界面副本，不改变执行器 Schema。</summary>
internal static class PuloniaOneDragonSettingChoices
{
    /// <summary>将秘境、首领和战斗策略展示为旧一条龙的下拉框，并展开战斗对象。</summary>
    internal static JObject Decorate(string name, JObject schema, JToken? value, string taskType)
    {
        var result = (JObject)schema.DeepClone();
        var key = System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
        string[]? choices = key switch
        {
            "domain_name" => ["", .. MapLazyAssets.Get().DomainNameList],
            "boss_name" => ["", .. AutoBossData.SupportedBossNames],
            "country" when taskType == "builtin.auto_ley_line" => [.. TaskSettingsPageViewModel.LeyLineOutcropCountryList],
            "ley_line_outcrop_type" => ["启示之花", "藏金之花"],
            "strategy_name" => GetStrategies(taskType == "builtin.auto_domain"),
            "sunday_selected_value" => ["", "1", "2", "3"],
            "max_artifact_star" => ["1", "2", "3", "4"],
            _ => null
        };
        if (choices is not null)
        {
            // 外部导入的暂不可用选项仍显示，用户可以主动选择替代项。
            result["enum"] = new JArray(choices.Concat(value?.Type == JTokenType.String ? [value.Value<string>()!] : Array.Empty<string>()).Distinct());
        }
        result["description"] = key switch
        {
            "domain_name" => "选择要刷取的秘境。",
            "boss_name" => "选择要讨伐的首领。",
            "country" => "选择任务前往的国家。",
            "party_name" or "team_name" or "team" or "fight_team_name" => "填写游戏内队伍名称，留空时使用当前队伍。",
            "strategy_name" => "选择当前任务使用的战斗策略。",
            "round_count" => "0 表示按树脂配置刷取，其余值表示指定轮数。",
            "is_resin_exhaustion_mode" => "开启后持续刷取至树脂耗尽。",
            "resin_priority_list" => "按列表顺序使用树脂。",
            "weekday_overrides" => "按周日到周六安排任务，服务器时间凌晨四点切换日期。",
            "boss_num" => "选择幽境危战中的首领序号（1—3）。",
            "fight_config" or "finish_detect_config" => "展开设置当前任务的战斗与结束检测方式。",
            _ => result.Value<string>("description") ?? "未修改的设置继续使用参数来源中的值。"
        };
        if (value is JObject values && key is not ("weekday_overrides" or "settings"))
        {
            var properties = result["properties"] as JObject ?? new JObject(values.Properties()
                .Select(p => new JProperty(p.Name, InferSchema(p.Value))));
            result["properties"] = new JObject(properties.Properties().Select(p => new JProperty(p.Name,
                Decorate(p.Name, (JObject)p.Value, values[p.Name], taskType))));
        }
        if (key == "resin_priority_list" && value is JArray priority)
        {
            // 优先顺序使用与老一条龙一致的树脂下拉框，持久化仍是原有字符串数组。
            result["properties"] = new JObject(priority.Select((item, index) => new JProperty(index.ToString(), new JObject
            {
                ["type"] = "string", ["title"] = $"第 {index + 1} 优先",
                ["enum"] = new JArray(new[] { "浓缩树脂", "原粹树脂", "须臾树脂", "脆弱树脂" }
                    .Append(item.Value<string>() ?? "").Distinct())
            })));
        }
        return result;
    }

    /// <summary>以七个日期卡片编辑每周安排，未修改的日期不生成覆盖。</summary>
    internal static JObject WeekdaySchema(JObject properties, JObject values, string taskType)
    {
        var days = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        var keys = new[] { "domain_name", "party_name", "strategy_name", "round_count", "ley_line_outcrop_type", "country", "team", "count" };
        return new JObject
        {
            ["type"] = "object", ["title"] = "每周安排", ["description"] = "按服务器时间凌晨四点切换日期，未设置的日期沿用当前任务设置。",
            ["properties"] = new JObject(days.Select((day, index) => new JProperty(index.ToString(), new JObject
            {
                ["type"] = "object", ["title"] = day,
                ["properties"] = new JObject(new[] { new JProperty("enabled", new JObject
                    { ["type"] = "boolean", ["title"] = "当天执行", ["default"] = true }) }
                    .Concat(keys.Where(properties.ContainsKey).Select(key =>
                    {
                        var schema = Decorate(key, (JObject)properties[key]!, values[key], taskType);
                        if (values[key] is { } initial) schema["default"] = initial.DeepClone();
                        return new JProperty(key, schema);
                    })))
            })))
        };
    }

    /// <summary>只有值没有对象子 Schema 的额外参数，按现有 JSON 类型建立展示字段。</summary>
    private static JObject InferSchema(JToken value) => new()
    {
        ["type"] = value.Type switch
        {
            JTokenType.Boolean => "boolean", JTokenType.Integer => "integer", JTokenType.Float => "number",
            JTokenType.Object => "object", JTokenType.Array => "array", JTokenType.Null => new JArray("string", "null"), _ => "string"
        }
    };

    /// <summary>读取策略名称，不创建旧自动战斗视图模型或读取其选择。</summary>
    private static string[] GetStrategies(bool allowCombo)
    {
        var directory = Global.Absolute(@"User\AutoFight");
        var scripts = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.txt").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray() : [];
        return ["根据队伍自动选择", .. allowCombo ? new[] { AutoFightParam.ComboStrategyName } : Array.Empty<string>(), .. scripts];
    }
}
