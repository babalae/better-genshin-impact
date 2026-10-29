using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.ResinPlan;
using BetterGenshinImpact.GameTask.SoloTask;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>
/// 体力计划中一个子任务的设置说明：参数键、隐藏字段、摘要字段及战斗策略字段的联动方式。
/// 隐藏字段与 ResinPlanTask 在运行时改写的字段一致，界面不让用户编辑不会生效的值。
/// </summary>
/// <param name="Key">体力计划参数中的键。</param>
/// <param name="TaskId">子任务标识。</param>
/// <param name="DisplayName">子任务显示名。</param>
/// <param name="Description">在体力计划中的作用说明。</param>
/// <param name="HiddenFields">由体力计划决定、设置弹窗中不显示的字段。</param>
/// <param name="SummaryFields">卡片摘要展示的字段及标签。</param>
/// <param name="StrategyField">设置弹窗中展示的战斗策略字段；没有联动时为 null。</param>
/// <param name="StrategyMirrorField">与战斗策略联动的隐藏字段；任务优先使用它，修改策略时一并更新。</param>
/// <param name="ClearMirror">修改策略时清空联动字段（true），或写入相同的策略（false）。</param>
/// <param name="FieldTaskType">设置卡片按哪种任务类型补充选项与树脂编辑器；null 时使用 solo.{TaskId}。</param>
/// <param name="ResinCapability">设置弹窗中树脂使用字段采用的能力；null 表示树脂使用由体力计划决定、不显示。</param>
public sealed record ResinPlanSubTaskSpec(
    string Key,
    string TaskId,
    string DisplayName,
    string Description,
    IReadOnlyList<string> HiddenFields,
    IReadOnlyList<(string Field, string Label)> SummaryFields,
    string? StrategyField = null,
    string? StrategyMirrorField = null,
    bool ClearMirror = false,
    string? FieldTaskType = null,
    ResinUsageCapability? ResinCapability = null)
{
    /// <summary>体力计划内首领树脂编辑器的能力键：只用原粹，不提供须臾、脆弱补充。</summary>
    public const string BossResinTaskKey = "resin_plan.boss";

    /// <summary>
    /// 自动秘境：根据提升指南养成；树脂、秘境与奖励识别由体力计划决定，备选秘境用于剩余树脂，
    /// 盛材移涌与祝圣移涌的秘境单独选择（默认跟随培养计划）。
    /// </summary>
    public static ResinPlanSubTaskSpec Domain { get; } = new("domain", AutoDomainTask.TaskId, "自动秘境",
        "按游戏内养成计划（冒险之证 → 提升指南）选择秘境，可连续刷多个秘境；剩余树脂可刷备选秘境；移涌活动的秘境在设置中选择，默认跟随培养计划。",
        ["resin_usage", "domain_name", "sunday_selected_value", "training_guide_calculate_runs_enabled",
            "reward_recognition_enabled", "target_items", "training_targets_json"],
        // 移涌活动秘境为空表示跟随培养计划，摘要只显示手动指定的秘境
        [("party_name", "队伍"), ("training_guide_fallback_domain_name", "备选秘境"),
            ("material_overflow_domain_name", "盛材移涌"), ("blessing_overflow_domain_name", "祝圣移涌")]);

    /// <summary>
    /// 自动首领讨伐：次数沿用首领自己的树脂使用设置（指定次数或刷至耗尽），在体力计划中只用原粹，不使用须臾、脆弱补充。
    /// </summary>
    public static ResinPlanSubTaskSpec Boss { get; } = new("boss", AutoBossTask.TaskId, "自动首领讨伐",
        "按首领的树脂使用设置讨伐，只使用原粹树脂；战斗不稳定，默认不开启。",
        ["reward_recognition_enabled", "target_items", "combat_strategy_path"],
        [("boss_name", "首领"), ("team_name", "队伍")],
        "strategy_name", "combat_strategy_path",
        FieldTaskType: BossResinTaskKey, ResinCapability: ResinPlanBudget.BossCapability);

    /// <summary>自动幽境危战：剩余树脂的第一项，紊乱爆发期结束后自动跳过。</summary>
    public static ResinPlanSubTaskSpec Stygian { get; } = new("stygian", AutoStygianOnslaughtTask.TaskId, "自动幽境危战",
        "消耗剩余树脂；只有紊乱爆发期能领取，爆发期结束后自动跳过。",
        ["resin_usage", "combat_script_bag_path", "dire_prestige_target"],
        [("boss_num", "首领序号"), ("fight_team_name", "队伍")],
        "strategy_name", "combat_script_bag_path", ClearMirror: true);

    /// <summary>自动地脉花：在幽境危战之后消耗剩余树脂。</summary>
    public static ResinPlanSubTaskSpec LeyLine { get; } = new("ley_line", AutoLeyLineOutcropTask.TaskId, "自动地脉花",
        "消耗剩余树脂，按设置的国家与地脉花类型刷取。",
        ["resin_usage", "target_items"],
        [("country", "国家"), ("ley_line_outcrop_type", "类型")]);

    /// <summary>
    /// 读取子任务的树脂使用设置，并去掉体力计划不使用的树脂种类，供界面展示。
    /// 只用于展示：运行时体力计划自行生成树脂设置，参数中的原值不被改写。
    /// </summary>
    /// <param name="param">子任务参数。</param>
    /// <returns>展示用的树脂设置；树脂使用由体力计划决定时为 null。</returns>
    public ResinUsage? ReadResinUsage(JObject param)
    {
        if (ResinCapability is not { } capability)
        {
            return null;
        }

        ResinUsage usage;
        try
        {
            usage = param["resin_usage"] is JObject value ? SoloTaskJson.Deserialize<ResinUsage>(value.ToString()) : capability.Defaults;
        }
        catch (System.Text.Json.JsonException)
        {
            usage = capability.Defaults;
        }

        var allowed = usage.Allowed.Clone();
        foreach (var kind in ResinKinds.InOrder)
        {
            if (!capability.IsSupported(kind))
            {
                allowed[kind] = false;
            }
        }

        return usage with { Allowed = allowed };
    }
}
