using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service.I18n;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 共享设置目录：代码注册的全部设置项，不持久化。首批覆盖地图追踪、JS 与 Shell 的宿主设置，
/// 值类型与默认值取自宿主参数的 Schema 和默认值，保证“未设置”与能力默认值一致。
/// </summary>
public static class PuloniaSettingCatalog
{
    /// <summary>队伍与角色类别。</summary>
    public const string PartyCategory = "party";

    /// <summary>战斗类别。</summary>
    public const string FightCategory = "fight";

    /// <summary>战斗结束检测类别。</summary>
    public const string FightEndCategory = "fight_end";

    /// <summary>战斗中视觉识别类别。</summary>
    public const string FightVisionCategory = "fight_vision";

    /// <summary>拾取类别。</summary>
    public const string PickupCategory = "pickup";

    /// <summary>行走类别。</summary>
    public const string TravelCategory = "travel";

    /// <summary>赶路类别。</summary>
    public const string HurryCategory = "hurry";

    /// <summary>食物类别。</summary>
    public const string EatCategory = "eat";

    /// <summary>路线 CD 类别；设置项 ID 为 <c>route.cooldown</c>。</summary>
    public const string RouteCategory = "route";

    /// <summary>Shell 类别。</summary>
    public const string ShellCategory = "shell";

    /// <summary>录制回放类别。</summary>
    public const string KeyMouseCategory = "keymouse";

    /// <summary>路线 CD 设置项 ID。</summary>
    public const string RouteCooldownKey = "route.cooldown";

    /// <summary>条件选队设置项 ID。</summary>
    public const string PartySourceKey = "party.source";

    /// <summary>地图追踪与 JS 共用的宿主设置类别。</summary>
    public static readonly string[] HostCategories =
        [PartyCategory, FightCategory, FightEndCategory, FightVisionCategory, PickupCategory, TravelCategory, HurryCategory, EatCategory];

    /// <summary>全部类别，按编辑器顺序排列。</summary>
    public static IReadOnlyList<PuloniaSettingCategory> Categories { get; } =
    [
        new(PartyCategory, I18nService.Instance.Translate("队伍与角色"), 10),
        new(FightCategory, I18nService.Instance.Translate("战斗"), 20),
        new(FightEndCategory, I18nService.Instance.Translate("战斗结束检测"), 30),
        new(FightVisionCategory, I18nService.Instance.Translate("战斗中视觉识别"), 40),
        new(PickupCategory, I18nService.Instance.Translate("拾取"), 50),
        new(TravelCategory, I18nService.Instance.Translate("行走"), 60),
        new(HurryCategory, I18nService.Instance.Translate("赶路"), 70),
        new(EatCategory, I18nService.Instance.Translate("食物"), 80),
        new(RouteCategory, I18nService.Instance.Translate("路线 CD"), 90),
        new(KeyMouseCategory, I18nService.Instance.Translate("录制回放"), 95),
        new(ShellCategory, "Shell", 100)
    ];

    /// <summary>宿主参数 Schema：地图追踪公共参数加路线 CD；JS 使用其中除路线 CD 外的部分。</summary>
    private static readonly JObject HostSchema = CreateHostSchema();

    /// <summary>宿主参数默认值，与地图追踪能力默认值相同。</summary>
    private static readonly JObject HostDefaults = PuloniaTaskCommonSettings.CreateDefaults("pathing");

    /// <summary>Shell 宿主参数 Schema。</summary>
    private static readonly JObject ShellSchema = PuloniaTaskCommonSettings.CreateSchemaProperties("shell");

    /// <summary>Shell 宿主参数默认值。</summary>
    private static readonly JObject ShellDefaults = PuloniaTaskCommonSettings.CreateDefaults("shell");

    /// <summary>录制回放参数 Schema，与录制回放能力说明一致。</summary>
    private static readonly JObject KeyMouseSchema = new() { ["with_delay"] = new JObject { ["type"] = "boolean" } };

    /// <summary>录制回放参数默认值，与录制回放能力说明一致。</summary>
    private static readonly JObject KeyMouseDefaults = new() { ["with_delay"] = false };

    /// <summary>类别内排序计数，仅在静态初始化期间使用。</summary>
    private static readonly Dictionary<string, int> OrderCounters = new(StringComparer.Ordinal);

    /// <summary>全部设置项，按类别和类别内顺序排列。</summary>
    public static IReadOnlyList<PuloniaSettingDefinition> Definitions { get; } = CreateDefinitions();

    /// <summary>按 ID 索引的设置项。</summary>
    private static readonly IReadOnlyDictionary<string, PuloniaSettingDefinition> ByKey =
        Definitions.ToDictionary(item => item.Key, StringComparer.Ordinal);

    /// <summary>查找设置项。</summary>
    public static bool TryGet(string key, out PuloniaSettingDefinition definition)
        => ByKey.TryGetValue(key, out definition!);

    /// <summary>取得设置项，不存在时抛出异常。</summary>
    public static PuloniaSettingDefinition Get(string key)
        => ByKey.TryGetValue(key, out var definition)
            ? definition
            : throw new KeyNotFoundException("未知的共享设置项：" + key);

    /// <summary>取得类别。</summary>
    public static PuloniaSettingCategory GetCategory(string id) => Categories.First(item => item.Id == id);

    /// <summary>
    /// 为宿主能力生成绑定：所选类别中的全部设置项映射到各自的参数路径。
    /// </summary>
    /// <param name="categories">能力使用的类别。</param>
    public static IReadOnlyDictionary<string, string> CreateBindings(params string[] categories)
        => Definitions.Where(item => categories.Contains(item.Category))
            .ToDictionary(item => item.Key, item => item.ParameterPath, StringComparer.Ordinal);

    /// <summary>
    /// 判断设置项的条件是否全部成立。
    /// </summary>
    /// <param name="conditions">条件列表。</param>
    /// <param name="valueOf">取得设置项有效值。</param>
    public static bool Evaluate(IReadOnlyList<PuloniaSettingCondition> conditions, Func<string, JToken?> valueOf)
        => conditions.All(condition =>
        {
            var value = valueOf(condition.Key) ?? JValue.CreateNull();
            var contained = condition.Values.Any(item => JToken.DeepEquals(item, value));
            return condition.Negate ? !contained : contained;
        });

    /// <summary>建立全部设置项；顺序即编辑器中的显示顺序。</summary>
    private static List<PuloniaSettingDefinition> CreateDefinitions()
    {
        var avatarChoices = new List<PuloniaSettingChoice>
        {
            new("", I18nService.Instance.Translate("不指定")), new("1", I18nService.Instance.Translate("1 号位")), new("2", I18nService.Instance.Translate("2 号位")), new("3", I18nService.Instance.Translate("3 号位")), new("4", I18nService.Instance.Translate("4 号位"))
        };
        var fixedParty = new[] { When(PartySourceKey, "fixed") };
        var conditionReason = I18nService.Instance.Translate("由地图追踪条件配置决定");
        var hasGuardian = new[] { When(PartySourceKey, "fixed"), Unless("party.guardian_avatar_index", "") };
        var fightGuardian = new[] { Unless("fight.guardian_avatar", "") };
        var finishDetect = new[] { When("fight_end.enabled", true) };
        var rotate = new[] { When("fight_end.enabled", true), When("fight_end.rotate_find_enemy_enabled", true) };
        var hurry = new[] { Unless("hurry.avatar", "") };
        var mavuika = new[] { When("hurry.avatar", "自动", "玛薇卡") };
        var jumpFly = new[] { When("hurry.avatar", "自动", "玛薇卡"), When("hurry.mwk_jump_fly_enabled", true) };
        return
        [
            // 队伍与角色
            Define(PartyCategory, "source", PuloniaTaskCommonSettings.PartySourceParameter, I18nService.Instance.Translate("队伍与角色位来源"),
                I18nService.Instance.Translate("使用下方设置：按填写的队伍和角色位执行；按地图追踪条件配置：按条件配置自动选择队伍、行走位和生存位，其余设置不受影响。"),
                choices: [new("fixed", I18nService.Instance.Translate("使用下方设置")), new("condition", I18nService.Instance.Translate("按地图追踪条件配置"))]),
            Define(PartyCategory, "party_name", "party_name", I18nService.Instance.Translate("切换到的队伍"),
                I18nService.Instance.Translate("游戏中手动设置的队伍名称，为空则不切换。"), enabledWhen: fixedParty, disabledReason: conditionReason),
            Define(PartyCategory, "skip_party_switch", "skip_party_switch", I18nService.Instance.Translate("跳过队伍切换"),
                I18nService.Instance.Translate("开启后不切换队伍，直接使用当前队伍执行。")),
            Define(PartyCategory, "visit_statue_before_switch", "is_visit_statue_before_switch_party", I18nService.Instance.Translate("切换队伍前前往神像"),
                I18nService.Instance.Translate("在七天神像切换队伍，避免异常状态导致切换失败。"), enabledWhen: fixedParty, disabledReason: conditionReason),
            Define(PartyCategory, "main_avatar_index", "main_avatar_index", I18nService.Instance.Translate("行走位"),
                I18nService.Instance.Translate("主要行走的角色在队伍内的编号；推荐与生存位相同，没有特殊需求无需配置。"),
                choices: avatarChoices, enabledWhen: fixedParty, disabledReason: conditionReason),
            Define(PartyCategory, "guardian_avatar_index", "guardian_avatar_index", I18nService.Instance.Translate("生存位"),
                I18nService.Instance.Translate("开盾或回血的角色在队伍内的编号，推荐绮良良、莱依拉。"),
                choices: avatarChoices, enabledWhen: fixedParty, disabledReason: conditionReason),
            Define(PartyCategory, "guardian_skill_interval", "guardian_elemental_skill_second_interval", I18nService.Instance.Translate("生存位战技间隔"),
                I18nService.Instance.Translate("生存位释放元素战技的间隔，可略大于实际技能 CD。"), unit: I18nService.Instance.Translate("秒"), visibleWhen: hasGuardian),
            Define(PartyCategory, "guardian_skill_long_press", "guardian_elemental_skill_long_press", I18nService.Instance.Translate("生存位长按战技"),
                I18nService.Instance.Translate("钟离长按开盾、绮良良长按变猫等。"), visibleWhen: hasGuardian),

            // 战斗
            Define(FightCategory, "strategy_name", "auto_fight_config.StrategyName", I18nService.Instance.Translate("战斗策略"),
                editor: PuloniaSettingEditorKind.CombatStrategy),
            Define(FightCategory, "timeout", "auto_fight_config.Timeout", I18nService.Instance.Translate("战斗超时"), I18nService.Instance.Translate("到达时间后自动停止战斗。"), unit: I18nService.Instance.Translate("秒")),
            Define(FightCategory, "action_scheduler_by_cd", "auto_fight_config.ActionSchedulerByCd", I18nService.Instance.Translate("按技能 CD 跳过出招"),
                I18nService.Instance.Translate("元素战技 CD 未结束时跳过此人出招。填人名或“人名,秒数”，多项用分号分隔，例如：白术;钟离,12;")),
            Define(FightCategory, "swimming_enabled", "auto_fight_config.SwimmingEnabled", I18nService.Instance.Translate("游泳检测"),
                I18nService.Instance.Translate("战斗中进入游泳时先回战斗节点，失败则前往七天神像。")),
            Define(FightCategory, "team_names", "auto_fight_config.TeamNames", I18nService.Instance.Translate("强制指定队伍角色"),
                I18nService.Instance.Translate("不识别队伍，直接按填写的角色名战斗，用英文逗号分隔。"), advanced: true),
            Define(FightCategory, "guardian_avatar", "auto_fight_config.GuardianAvatar", I18nService.Instance.Translate("盾奶位优先释放技能"),
                I18nService.Instance.Translate("战斗中实时检测并优先释放该位置角色的技能，选“不指定”为关闭。"), advanced: true, choices: avatarChoices),
            Define(FightCategory, "guardian_combat_skip", "auto_fight_config.GuardianCombatSkip", I18nService.Instance.Translate("盾奶位自动释放 E"),
                I18nService.Instance.Translate("禁用该角色的战斗策略，改为自动释放元素战技。"), advanced: true, visibleWhen: fightGuardian),
            Define(FightCategory, "burst_enabled", "auto_fight_config.BurstEnabled", I18nService.Instance.Translate("盾奶位自动释放 Q"),
                I18nService.Instance.Translate("禁用该角色的战斗策略，改为自动释放元素爆发。"), advanced: true, visibleWhen: fightGuardian),
            Define(FightCategory, "guardian_avatar_hold", "auto_fight_config.GuardianAvatarHold", I18nService.Instance.Translate("盾奶位长按战技"),
                I18nService.Instance.Translate("关闭为短按，开启为长按。"), advanced: true, visibleWhen: fightGuardian),

            // 战斗结束检测
            Define(FightEndCategory, "enabled", "auto_fight_config.FightFinishDetectEnabled", I18nService.Instance.Translate("自动检测战斗结束"),
                I18nService.Instance.Translate("检测到战斗已经结束时停止自动战斗。")),
            Define(FightEndCategory, "fast_check_enabled", "auto_fight_config.FinishDetectConfig.FastCheckEnabled", I18nService.Instance.Translate("更快触发结束检查"),
                I18nService.Instance.Translate("完成一轮动作后，满足条件即触发一次战斗结束检查。"), visibleWhen: finishDetect),
            Define(FightEndCategory, "fast_check_params", "auto_fight_config.FinishDetectConfig.FastCheckParams", I18nService.Instance.Translate("快速检查参数"),
                I18nService.Instance.Translate("填秒数时距上次检查超过该时间触发检查，填人名时该角色动作后触发；多项用分号分隔，例如 5;白术;"),
                advanced: true, visibleWhen: [When("fight_end.enabled", true), When("fight_end.fast_check_enabled", true)]),
            Define(FightEndCategory, "rotate_find_enemy_enabled", "auto_fight_config.FinishDetectConfig.RotateFindEnemyEnabled", I18nService.Instance.Translate("旋转寻找敌人位置"),
                I18nService.Instance.Translate("（实验性）检查战斗结束前先检测敌人，判断是否需要靠近或旋转寻找；与“敌人可见时跳过结束检查”互斥。"), visibleWhen: finishDetect),
            Define(FightEndCategory, "rotary_factor", "auto_fight_config.FinishDetectConfig.RotaryFactor", I18nService.Instance.Translate("旋转速度"),
                I18nService.Instance.Translate("1—13，越大越快，建议单次旋转约 360°。"), advanced: true, visibleWhen: rotate),
            Define(FightEndCategory, "check_before_burst", "auto_fight_config.FinishDetectConfig.CheckBeforeBurst", I18nService.Instance.Translate("Q 前检查"),
                I18nService.Instance.Translate("释放元素爆发前检测战斗是否已经结束。"), advanced: true, visibleWhen: rotate),
            Define(FightEndCategory, "is_first_check", "auto_fight_config.FinishDetectConfig.IsFirstCheck", I18nService.Instance.Translate("尝试面敌"),
                I18nService.Instance.Translate("开战时寻敌，战斗中尝试面向敌人。"), advanced: true, visibleWhen: rotate),
            Define(FightEndCategory, "skip_check_when_enemy_visible", "auto_fight_config.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible",
                I18nService.Instance.Translate("敌人可见时跳过结束检查"), I18nService.Instance.Translate("检测到敌人血条时跳过战斗结束检查；与“旋转寻找敌人位置”互斥。"),
                advanced: true, visibleWhen: finishDetect),
            Define(FightEndCategory, "check_after_switch_avatar", "auto_fight_config.FinishDetectConfig.CheckAfterSwitchAvatar", I18nService.Instance.Translate("切人后再检查"),
                I18nService.Instance.Translate("切人后立即检查战斗结束，无需等待上一个动作后摇；目前仅 JSON 策略生效。"), advanced: true, visibleWhen: finishDetect),
            Define(FightEndCategory, "block_check_seconds", "auto_fight_config.FinishDetectConfig.BlockCheckBeforeBattleSeconds", I18nService.Instance.Translate("开战后阻断结束检查"),
                I18nService.Instance.Translate("开战后该时间内的结束检查直接视为战斗未结束，0 表示不阻断。"), advanced: true, unit: I18nService.Instance.Translate("秒"), visibleWhen: finishDetect),
            Define(FightEndCategory, "paimon_check_enabled", "auto_fight_config.FinishDetectConfig.PaimonEndCheckEnabled", I18nService.Instance.Translate("派蒙辅助检测"),
                I18nService.Instance.Translate("按 L 后派蒙头像可见时提前跳出战斗结束检测。"), visibleWhen: finishDetect),
            Define(FightEndCategory, "paimon_check_delay", "auto_fight_config.FinishDetectConfig.PaimonEndCheckDelay", I18nService.Instance.Translate("派蒙检测延时"),
                I18nService.Instance.Translate("按 L 后等待该时间再检测派蒙头像。"), advanced: true, unit: I18nService.Instance.Translate("秒"),
                visibleWhen: [When("fight_end.enabled", true), When("fight_end.paimon_check_enabled", true)]),
            Define(FightEndCategory, "check_end_delay", "auto_fight_config.FinishDetectConfig.CheckEndDelay", I18nService.Instance.Translate("检查前延时"),
                I18nService.Instance.Translate("触发结束检查前先等待，确保动作后摇结束；可为角色单独指定，例如 0.4;钟离,1.5"), advanced: true, visibleWhen: finishDetect),
            Define(FightEndCategory, "before_detect_delay", "auto_fight_config.FinishDetectConfig.BeforeDetectDelay", I18nService.Instance.Translate("按键后检查延时"),
                I18nService.Instance.Translate("按下切换队伍后检查屏幕色块的延时；无法结束战斗时可适当提高，例如 0.75，但不要大到真的打开队伍界面。"),
                advanced: true, unit: I18nService.Instance.Translate("秒"), visibleWhen: finishDetect),

            // 战斗中视觉识别
            Define(FightVisionCategory, "combat_targeting", "auto_fight_config.EnableCombatTargeting", I18nService.Instance.Translate("战斗中持续索敌"),
                I18nService.Instance.Translate("战斗过程中情况允许时持续尝试面朝敌人。"), advanced: true),
            Define(FightVisionCategory, "lock_lost_wait_time", "auto_fight_config.LockLostWaitTime", I18nService.Instance.Translate("脱锁等待时间"),
                I18nService.Instance.Translate("敌人不可见时等待该时间后开始旋转索敌。"), advanced: true, unit: I18nService.Instance.Translate("秒")),
            Define(FightVisionCategory, "targeting_interval", "auto_fight_config.TargetingDetectionInterval", I18nService.Instance.Translate("索敌识别间隔"),
                advanced: true, unit: I18nService.Instance.Translate("毫秒")),
            Define(FightVisionCategory, "damage_number_mode", "auto_fight_config.DamageNumberRecognitionMode", I18nService.Instance.Translate("伤害数字识别方式"),
                I18nService.Instance.Translate("血条不可见或血条位置固定时，用伤害数字辅助定位敌人。"), advanced: true,
                choices: [new(0, I18nService.Instance.Translate("禁用")), new(1, I18nService.Instance.Translate("OCR 识别")), new(2, I18nService.Instance.Translate("颜色识别"))]),
            Define(FightVisionCategory, "draw_results", "auto_fight_config.DrawRecognitionResults", I18nService.Instance.Translate("绘制识别结果"),
                I18nService.Instance.Translate("在遮罩窗口上显示血条、伤害数字等识别结果的边框。"), advanced: true),

            // 拾取
            Define(PickupCategory, "auto_pick", "auto_pick_enabled", I18nService.Instance.Translate("自动拾取"), I18nService.Instance.Translate("拾取路线中的掉落物和采集物。")),
            Define(PickupCategory, "after_fight_scan", "auto_fight_config.PickDropsAfterFightEnabled", I18nService.Instance.Translate("战后扫描掉落物光柱"),
                I18nService.Instance.Translate("战斗结束后旋转视角寻找掉落物光柱并靠近（没有万叶和琴时使用）。")),
            Define(PickupCategory, "after_fight_scan_seconds", "auto_fight_config.PickDropsAfterFightSeconds", I18nService.Instance.Translate("光柱扫描最长时间"),
                unit: I18nService.Instance.Translate("秒"), visibleWhen: [When("pickup.after_fight_scan", true)]),
            Define(PickupCategory, "kazuha", "auto_fight_config.KazuhaPickupEnabled", I18nService.Instance.Translate("万叶或琴聚集材料"),
                I18nService.Instance.Translate("战斗结束后由万叶或琴长按元素战技聚集材料。")),
            Define(PickupCategory, "kazuha_party_name", "auto_fight_config.KazuhaPartyName", I18nService.Instance.Translate("万叶拾取队伍"),
                I18nService.Instance.Translate("队伍中没有万叶时切换到这个队伍拾取，为空则不切换。"), visibleWhen: [When("pickup.kazuha", true)]),
            Define(PickupCategory, "qin_double", "auto_fight_config.QinDoublePickUp", I18nService.Instance.Translate("二次拾取"),
                I18nService.Instance.Translate("首次拾取为空时再次拾取，即使万叶刚用过技能也会强制拾取。"), advanced: true),
            Define(PickupCategory, "exp_based", "auto_fight_config.ExpBasedPickupEnabled", I18nService.Instance.Translate("按经验值判断是否拾取"),
                I18nService.Instance.Translate("未检测到精英怪经验值图标时跳过战后拾取。"), advanced: true),
            Define(PickupCategory, "battle_threshold", "auto_fight_config.BattleThresholdForLoot", I18nService.Instance.Translate("拾取战斗人次阈值"),
                I18nService.Instance.Translate("战斗人次小于该值时不执行战后拾取，不小于 2 才生效；留空表示不限制。"), advanced: true),
            Define(PickupCategory, "only_elite_mode", "auto_fight_config.OnlyPickEliteDropsMode", I18nService.Instance.Translate("只拾取精英掉落"),
                I18nService.Instance.Translate("需要路线标记了怪物类型；非精英点位按所选方式处理拾取。"),
                choices: [new("Closed", I18nService.Instance.Translate("关闭")), new("AllowAutoPickupForNonElite", I18nService.Instance.Translate("非精英允许自动拾取")),
                    new("DisableAutoPickupForNonElite", I18nService.Instance.Translate("非精英关闭自动拾取"))]),

            // 行走
            Define(TravelCategory, "auto_run", "auto_run_enabled", I18nService.Instance.Translate("自动冲刺")),
            Define(TravelCategory, "auto_skip", "auto_skip_enabled", I18nService.Instance.Translate("自动跳过剧情"), I18nService.Instance.Translate("行走途中意外进入剧情时自动脱离。")),
            Define(TravelCategory, "gadget_interval", "use_gadget_interval_ms", I18nService.Instance.Translate("小道具使用间隔"),
                I18nService.Instance.Translate("移动期间自动使用小道具，0 为不使用。"), unit: I18nService.Instance.Translate("毫秒")),
            Define(TravelCategory, "recover_timing", "recover_timing", I18nService.Instance.Translate("低血量回复时机"),
                choices: [new(0, I18nService.Instance.Translate("任何路径点")), new(1, I18nService.Instance.Translate("只在传送点")), new(2, I18nService.Instance.Translate("不回复"))]),

            // 赶路
            Define(HurryCategory, "avatar", "hurry_on_avatar", I18nService.Instance.Translate("赶路角色"),
                I18nService.Instance.Translate("选择“自动”时优先用行走位赶路，不支持时尝试队伍中其他支持的角色。"),
                choices: [new("", I18nService.Instance.Translate("不赶路")), new("自动", I18nService.Instance.Translate("自动")), new("玛薇卡", I18nService.Instance.Translate("玛薇卡")), new("闲云", I18nService.Instance.Translate("闲云")), new("桑多涅", I18nService.Instance.Translate("桑多涅")),
                    new("恰斯卡", I18nService.Instance.Translate("恰斯卡")), new("流浪者", I18nService.Instance.Translate("流浪者")), new("伊法", I18nService.Instance.Translate("伊法")), new("希诺宁", I18nService.Instance.Translate("希诺宁")),
                    new("法尔伽", I18nService.Instance.Translate("法尔伽")), new("夜兰", I18nService.Instance.Translate("夜兰"))]),
            Define(HurryCategory, "travel_mode", "travel_mode", I18nService.Instance.Translate("赶路模式"), I18nService.Instance.Translate("精准靠近适合采集，节点间减速；连续赶路适合锄地。"),
                choices: [new("精准靠近", I18nService.Instance.Translate("精准靠近")), new("连续赶路", I18nService.Instance.Translate("连续赶路"))], visibleWhen: hurry),
            Define(HurryCategory, "distance", "distance", I18nService.Instance.Translate("赶路启用距离"), I18nService.Instance.Translate("节点距离大于此值时启用赶路技能，0 为关闭。"),
                unit: I18nService.Instance.Translate("米"), visibleWhen: hurry),
            Define(HurryCategory, "approach_stop_distance", "approach_stop_distance", I18nService.Instance.Translate("接近停车距离"),
                I18nService.Instance.Translate("到达节点前开始减速、下车或切人的距离，不能大于赶路启用距离。"), unit: I18nService.Instance.Translate("米"), visibleWhen: hurry),
            Define(HurryCategory, "switch_to_walk", "switch_to_walk_enabled", I18nService.Instance.Translate("接近时切人步行"), visibleWhen: hurry),
            Define(HurryCategory, "frame_interval", "hurry_on_frame_interval", I18nService.Instance.Translate("赶路帧间隔"),
                I18nService.Instance.Translate("设备性能富余时可适当降低以提高稳定性，有效范围 1—150。"), advanced: true, unit: I18nService.Instance.Translate("毫秒"), visibleWhen: hurry),
            Define(HurryCategory, "mwk_jump_fly_enabled", "mwk_jump_fly_enabled", I18nService.Instance.Translate("跳飞赶路"), visibleWhen: mavuika),
            Define(HurryCategory, "mwk_jump_fly_distance", "mwk_jump_fly_distance", I18nService.Instance.Translate("跳飞启用距离"),
                I18nService.Instance.Translate("节点距离大于此值时启用跳飞，需大于赶路启用距离。"), unit: I18nService.Instance.Translate("米"), visibleWhen: jumpFly),
            Define(HurryCategory, "mwk_jump_fly_interval", "mwk_jump_fly_interval_seconds", I18nService.Instance.Translate("跳飞或跳跃间隔"),
                I18nService.Instance.Translate("闲云实际使用一半的值。"), unit: I18nService.Instance.Translate("秒"), visibleWhen: [When("hurry.avatar", "自动", "玛薇卡", "闲云")]),
            Define(HurryCategory, "mwk_sprint_count", "mwk_jump_fly_sprint_count", I18nService.Instance.Translate("跳飞前额外冲刺次数"),
                I18nService.Instance.Translate("6 命玛薇卡可选，每次上车后前几次跳飞改为冲刺跳飞，推荐 3 次；0 为不使用。"), visibleWhen: jumpFly),
            Define(HurryCategory, "mwk_disable_sprint", "mwk_disable_sprint_enabled", I18nService.Instance.Translate("玛薇卡在车上禁用冲刺"),
                I18nService.Instance.Translate("0 命玛薇卡酌情开启，节约夜魂值。"), visibleWhen: mavuika),

            // 食物
            Define(EatCategory, "auto_eat", "auto_eat_enabled", I18nService.Instance.Translate("自动吃药"), I18nService.Instance.Translate("检测到红血时自动使用便携营养袋。")),
            Define(EatCategory, "atk_dish", "auto_eat_config.DefaultAtkBoostingDishName", I18nService.Instance.Translate("默认攻击类料理"),
                I18nService.Instance.Translate("供 JS 按食物类型 1 调用；填写不带“美味的”等前缀的名称。")),
            Define(EatCategory, "adventurers_dish", "auto_eat_config.DefaultAdventurersDishName", I18nService.Instance.Translate("默认冒险类料理"),
                I18nService.Instance.Translate("供 JS 按食物类型 2 调用。")),
            Define(EatCategory, "def_dish", "auto_eat_config.DefaultDefBoostingDishName", I18nService.Instance.Translate("默认防御类料理"),
                I18nService.Instance.Translate("供 JS 按食物类型 3 调用。")),

            // 路线 CD
            Define(RouteCategory, "cooldown", PuloniaRouteCooldown.ParameterName, I18nService.Instance.Translate("路线 CD"),
                I18nService.Instance.Translate("适用于采集、魔物、宝箱等地图追踪路线：按目录识别的刷新规则跳过仍在 CD 中的路线。"),
                editor: PuloniaSettingEditorKind.RouteCooldown),

            // 录制回放
            Define(KeyMouseCategory, "with_delay", "with_delay", I18nService.Instance.Translate("按录制延时回放"), I18nService.Instance.Translate("开启后按录制时的操作间隔回放；关闭时尽快连续回放。")),

            // Shell
            Define(ShellCategory, "disabled", "shell_disabled", I18nService.Instance.Translate("跳过 Shell 任务"), I18nService.Instance.Translate("开启后 Shell 任务直接跳过。")),
            Define(ShellCategory, "timeout_seconds", "shell_timeout_seconds", I18nService.Instance.Translate("Shell 超时"),
                I18nService.Instance.Translate("超过时间后结束进程，0 为不限时；节点执行策略中的超时仍然有效。"), unit: I18nService.Instance.Translate("秒")),
            Define(ShellCategory, "no_window", "shell_no_window", I18nService.Instance.Translate("隐藏命令窗口")),
            Define(ShellCategory, "output", "shell_output", I18nService.Instance.Translate("在日志中记录命令输出"), I18nService.Instance.Translate("命令执行成功且有输出时写入日志。"))
        ];
    }

    /// <summary>
    /// 定义一个设置项；值类型和默认值取自所属宿主参数，编辑提示未指定时按选项和值类型推断。
    /// </summary>
    private static PuloniaSettingDefinition Define(string category, string name, string path, string displayName,
        string description = "", bool advanced = false, PuloniaSettingEditorKind? editor = null,
        IReadOnlyList<PuloniaSettingChoice>? choices = null, string? unit = null,
        IReadOnlyList<PuloniaSettingCondition>? visibleWhen = null, IReadOnlyList<PuloniaSettingCondition>? enabledWhen = null,
        string? disabledReason = null)
    {
        var (schemaRoot, defaultsRoot) = category switch
        {
            ShellCategory => (ShellSchema, ShellDefaults),
            KeyMouseCategory => (KeyMouseSchema, KeyMouseDefaults),
            _ => (HostSchema, HostDefaults)
        };
        var schema = FindSchema(schemaRoot, path) ?? throw new InvalidOperationException($"设置项 {category}.{name} 的参数路径 {path} 不存在。");
        var defaultValue = FindValue(defaultsRoot, path) ?? throw new InvalidOperationException($"设置项 {category}.{name} 缺少默认值。");
        OrderCounters[category] = OrderCounters.GetValueOrDefault(category) + 1;
        return new PuloniaSettingDefinition
        {
            Key = category + "." + name,
            Category = category,
            Order = OrderCounters[category],
            DisplayName = displayName,
            Description = description,
            ParameterPath = path,
            ValueSchema = (JObject)schema.DeepClone(),
            DefaultValue = defaultValue.DeepClone(),
            IsAdvanced = advanced,
            Editor = editor ?? InferEditor(schema, choices),
            Choices = choices ?? [],
            Unit = unit,
            VisibleWhen = visibleWhen ?? [],
            EnabledWhen = enabledWhen ?? [],
            DisabledReason = disabledReason
        };
    }

    /// <summary>按选项和值类型推断编辑提示。</summary>
    private static PuloniaSettingEditorKind InferEditor(JObject schema, IReadOnlyList<PuloniaSettingChoice>? choices)
    {
        if (choices is { Count: > 0 })
            return PuloniaSettingEditorKind.Choice;
        var types = schema["type"] is JArray array ? array.Values<string>().ToArray() : [schema.Value<string>("type")];
        return types.Contains("boolean") ? PuloniaSettingEditorKind.Boolean
            : types.Contains("integer") ? PuloniaSettingEditorKind.Integer
            : types.Contains("number") ? PuloniaSettingEditorKind.Number
            : PuloniaSettingEditorKind.Text;
    }

    /// <summary>沿点分路径查找字段 Schema：首段在顶层属性中，后续段在嵌套对象的 properties 中。</summary>
    private static JObject? FindSchema(JObject properties, string path)
    {
        JObject? current = null;
        foreach (var segment in path.Split('.'))
        {
            var scope = current is null ? properties : current["properties"] as JObject;
            current = scope?[segment] as JObject;
            if (current is null)
                return null;
        }
        return current;
    }

    /// <summary>沿点分路径查找默认值。</summary>
    private static JToken? FindValue(JObject root, string path)
    {
        JToken? current = root;
        foreach (var segment in path.Split('.'))
        {
            current = (current as JObject)?[segment];
            if (current is null)
                return null;
        }
        return current;
    }

    /// <summary>建立宿主参数 Schema。</summary>
    private static JObject CreateHostSchema()
    {
        var properties = PuloniaTaskCommonSettings.CreateSchemaProperties("pathing");
        properties[PuloniaRouteCooldown.ParameterName] = PuloniaRouteCooldown.CreateSchema();
        return properties;
    }

    /// <summary>条件：设置项取给定值之一。</summary>
    private static PuloniaSettingCondition When(string key, params JToken[] values) => new(key, values);

    /// <summary>条件：设置项不取给定值。</summary>
    private static PuloniaSettingCondition Unless(string key, params JToken[] values) => new(key, values, true);
}
