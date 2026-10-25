using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.ViewModel.Resin;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙设置卡片的展示选项；只修饰界面副本，不改变执行器 Schema。</summary>
internal static class PuloniaOneDragonSettingChoices
{
    /// <summary>
    /// 自动秘境的任务类型；与独立自动战斗一样允许选择自动连招。
    /// </summary>
    private static readonly string DomainTaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoDomainTask.TaskId);

    /// <summary>
    /// 自动地脉花的任务类型；只有自动地脉花的国家使用地脉花国家列表。
    /// </summary>
    private static readonly string LeyLineTaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoLeyLineOutcropTask.TaskId);

    /// <summary>为秘境、首领补充展示选项，为战斗策略标记专用选择器，并展开战斗对象。</summary>
    internal static JObject Decorate(string name, JObject schema, JToken? value, string taskType)
    {
        var result = (JObject)schema.DeepClone();
        var key = System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
        // 仅装饰界面 Schema，执行合同仍为封闭的嵌套 resin_usage 对象。
        if (key == "resin_usage" && ResinUsageViewModelSupport.GetTask(taskType) is not null)
        {
            result["x-resin-usage"] = taskType;
            return result;
        }
        if (key is "strategy_name" or "combat_strategy_path")
        {
            result.Remove("enum");
            result["x-combat-strategy-kinds"] = (int)(taskType == DomainTaskType
                || taskType == PuloniaSoloTaskExecutor.ToTaskType(AutoFightEntryTask.TaskId)
                ? CombatStrategyKind.All : CombatStrategyKind.WithoutCombo);
        }
        string[]? choices = key switch
        {
            "domain_name" => ["", .. MapLazyAssets.Get().DomainNameList],
            "boss_name" => ["", .. AutoBossData.SupportedBossNames],
            "country" when taskType == LeyLineTaskType => [.. TaskSettingsPageViewModel.LeyLineOutcropCountryList],
            "ley_line_outcrop_type" => ["启示之花", "藏金之花"],
            // 战斗策略使用独立字段形态，由选择控件扫描目录。
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
            "strategy_name" or "combat_strategy_path" => "选择当前任务使用的战斗策略。",
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
        return result;
    }

    /// <summary>以七个日期卡片编辑每周安排，未修改的日期不生成覆盖。</summary>
    internal static JObject WeekdaySchema(JObject properties, JObject values, string taskType)
    {
        var days = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        // 只展示参数中存在的键；树脂设置按 resin_usage 对象整体覆盖。
        var keys = new[]
        {
            "domain_name", "party_name", "sunday_selected_value", "strategy_name", "combat_strategy_path",
            "round_count", "ley_line_outcrop_type", "country", "team", "count", "resin_usage"
        };
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

    // 策略目录统一由 CombatStrategyCatalog / CombatStrategyPicker 按需加载。
}
