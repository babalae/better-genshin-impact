using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
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

    /// <summary>为秘境、首领补充展示选项，为战斗策略与备选秘境标记专用选择器，并展开战斗对象。</summary>
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
        // 备选秘境与自动秘境设置页一致，使用按国家分组的秘境选择框，只列出真实秘境。
        // 体力计划的移涌活动秘境同样使用秘境选择框，只列出对应类型，并带“跟随培养计划 / 指定秘境”来源单选项；标记值为选项列表类型。
        var domainPicker = key switch
        {
            "training_guide_fallback_domain_name" => TrainingGuideDomainOptions.FallbackPicker,
            "material_overflow_domain_name" => TrainingGuideDomainOptions.MaterialOverflowPicker,
            "blessing_overflow_domain_name" => TrainingGuideDomainOptions.BlessingOverflowPicker,
            _ => null
        };
        if (domainPicker is not null)
        {
            result.Remove("enum");
            result["x-domain-picker"] = domainPicker;
        }
        string[]? choices = key switch
        {
            "domain_name" => ["", .. MapLazyAssets.Get().DomainNameList],
            "training_guide_fallback_sunday_selected_value" => ["", "1", "2", "3"],
            "boss_name" => ["", .. AutoBossData.SupportedBossNames],
            "country" when taskType == LeyLineTaskType => [.. TaskSettingsPageViewModel.LeyLineOutcropCountryList],
            // 选项是保存值，保持中文原值，只标记为 i18n Key，下拉框通过 TBinding 翻译展示
            "ley_line_outcrop_type" => [I18nService.Key("启示之花"), I18nService.Key("藏金之花")],
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
        // 说明只用于界面展示，按当前界面语言翻译；能力 Schema 自带的说明原样保留
        result["description"] = key switch
        {
            "domain_name" => I18nService.Instance.Translate("选择要刷取的秘境。"),
            "boss_name" => I18nService.Instance.Translate("选择要讨伐的首领。"),
            "country" => I18nService.Instance.Translate("选择任务前往的国家。"),
            "party_name" or "team_name" or "team" or "fight_team_name" => I18nService.Instance.Translate("填写游戏内队伍名称，留空时使用当前队伍。"),
            "strategy_name" or "combat_strategy_path" => I18nService.Instance.Translate("选择当前任务使用的战斗策略。"),
            "boss_num" => I18nService.Instance.Translate("选择幽境危战中的首领序号（1—3）。"),
            "fight_config" or "finish_detect_config" => I18nService.Instance.Translate("展开设置当前任务的战斗与结束检测方式。"),
            "training_guide_run_preference" => I18nService.Instance.Translate("0：适量刷取，依靠合成天赋收益补足材料；1：充分刷取，直到满足养成计划需求。"),
            "training_guide_crafting_bonus_reserve_percent" => I18nService.Instance.Translate("适量刷取时为合成额外收益预留的比例，范围 0–20。"),
            "training_guide_reward_recognition_enabled" => I18nService.Instance.Translate("每轮领奖后识别奖励并更新库存，减少多刷；需要 1920×1080 分辨率。"),
            "training_guide_reward_failure_budget_enabled" => I18nService.Instance.Translate("奖励识别失败时按首次估算的预算继续，关闭时识别失败即结束。"),
            "training_guide_low_ar_adaptation_enabled" => I18nService.Instance.Translate("冒险等阶较低时检查入口是否解锁，并自动降低难度。"),
            "training_guide_fallback_domain_name" => I18nService.Instance.Translate("养成需求全部满足后，用剩余树脂刷取的秘境；留空则保留剩余树脂。"),
            "training_guide_fallback_sunday_selected_value" => I18nService.Instance.Translate("备选秘境在周日或限时全开时领取的奖励序号。"),
            // 移涌活动秘境的来源说明随单选项变化，见 PuloniaOneDragonParameterFieldViewModel.DomainSourceDescription
            "material_overflow_domain_name" => I18nService.Instance.Translate("盛材移涌期间用原粹领取当天的双倍。"),
            "blessing_overflow_domain_name" => I18nService.Instance.Translate("祝圣移涌期间用原粹领取当天的双倍。"),
            _ => result.Value<string>("description") ?? I18nService.Instance.Translate("未修改的设置继续使用参数来源中的值。")
        };
        if (value is JObject values && key != "settings")
        {
            var properties = result["properties"] as JObject ?? new JObject(values.Properties()
                .Select(p => new JProperty(p.Name, InferSchema(p.Value))));
            result["properties"] = new JObject(properties.Properties().Select(p => new JProperty(p.Name,
                Decorate(p.Name, (JObject)p.Value, values[p.Name], taskType))));
        }
        return result;
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
