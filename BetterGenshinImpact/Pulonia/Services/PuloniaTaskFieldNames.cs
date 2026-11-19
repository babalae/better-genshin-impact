using System;
using System.Collections.Generic;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 任务参数与结果字段的中文显示名，参数编辑器、一条龙设置卡片和运行历史共用这一份映射。
/// 键为蛇形参数名；未登记的字段原样返回稳定字段名，便于排查扩展执行器数据。
/// </summary>
public static class PuloniaTaskFieldNames
{
    /// <summary>
    /// 字段名到中文显示名的映射；使用 Add 形式初始化，重复登记会在首次使用时直接失败。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // 1. 独立任务常用参数
        { "resin_usage", I18nService.Key("树脂使用") },
        { "domain_name", I18nService.Key("秘境") }, { "boss_name", I18nService.Key("首领") },
        { "strategy_name", I18nService.Key("战斗策略名称") }, { "combat_strategy_path", I18nService.Key("战斗策略") },
        { "team_name", I18nService.Key("队伍名称") }, { "team", I18nService.Key("队伍") }, { "fight_team_name", I18nService.Key("队伍") },
        { "revive_retry_count", I18nService.Key("死亡重试次数") }, { "timeout", I18nService.Key("战斗超时（秒）") },
        { "boss_num", I18nService.Key("幽境首领序号") },
        { "one_dragon_mode", I18nService.Key("地脉花一条龙模式") }, { "max_artifact_star", I18nService.Key("分解最大星级") },
        { "auto_artifact_salvage", I18nService.Key("结束后分解圣遗物") }, { "reward_recognition_enabled", I18nService.Key("识别奖励") },
        { "sunday_selected_value", I18nService.Key("周日奖励序号") }, { "ley_line_outcrop_type", I18nService.Key("地脉花类型") },
        { "fight_config", I18nService.Key("战斗设置") }, { "return_to_statue_after_each_round", I18nService.Key("每轮结束回神像") },
        { "friendship_team", I18nService.Key("好感队伍") },
        { "scan_drops_after_reward_enabled", I18nService.Key("领奖后扫描掉落物") },
        { "scan_drops_after_reward_seconds", I18nService.Key("扫描掉落物时长（秒）") }, { "fight_end_delay", I18nService.Key("战斗结束后等待（秒）") },
        { "short_movement", I18nService.Key("短距离移动") }, { "walk_to_f", I18nService.Key("走近交互") }, { "left_right_move_times", I18nService.Key("左右移动次数") },
        { "auto_eat", I18nService.Key("自动吃药") }, { "min_resin_to_keep", I18nService.Key("合成后保留原粹树脂") },
        { "teleport_type", I18nService.Key("进壶方式") }, { "secret_treasure_objects", I18nService.Key("洞天百宝购买") },
        { "wood_round_num", I18nService.Key("伐木轮数") }, { "wood_daily_max_count", I18nService.Key("每日伐木上限") },
        { "whole_process_timeout_seconds", I18nService.Key("钓鱼流程超时（秒）") }, { "throw_rod_time_out_timeout_seconds", I18nService.Key("抛竿超时（秒）") },
        { "fishing_time_policy", I18nService.Key("钓鱼时段") }, { "save_screenshot_on_key_tick", I18nService.Key("保存钓鱼关键步骤截图") },
        { "star", I18nService.Key("圣遗物最高星级") }, { "java_script", I18nService.Key("分解筛选脚本") }, { "artifact_set_filter", I18nService.Key("圣遗物套装筛选") },
        { "max_num_to_check", I18nService.Key("最多检查数量") }, { "recognition_failure_policy", I18nService.Key("识别失败时的处理方式") },
        { "codes", I18nService.Key("兑换码列表") }, { "target_items", I18nService.Key("材料目标") }, { "craft_count", I18nService.Key("指定合成份数") },

        // 1.1 自动秘境的提升指南设置
        { "training_guide_calculate_runs_enabled", I18nService.Key("按养成计划计算次数") },
        { "training_guide_run_preference", I18nService.Key("养成刷取偏好") },
        { "training_guide_crafting_bonus_reserve_percent", I18nService.Key("合成收益预留（%）") },
        { "training_guide_reward_recognition_enabled", I18nService.Key("按领奖结果更新养成库存") },
        { "training_guide_reward_failure_budget_enabled", I18nService.Key("奖励识别失败时按固定预算继续") },
        { "training_guide_diagnostics_enabled", I18nService.Key("保存养成诊断记录") },
        { "training_guide_low_ar_adaptation_enabled", I18nService.Key("低冒险等阶适配") },
        { "training_guide_fallback_domain_name", I18nService.Key("备选秘境") },
        { "training_guide_fallback_sunday_selected_value", I18nService.Key("备选秘境周日奖励序号") },
        { "material_overflow_domain_name", I18nService.Key("盛材移涌秘境") }, { "blessing_overflow_domain_name", I18nService.Key("祝圣移涌秘境") },
        { "training_targets_json", I18nService.Key("自定义培养目标") },

        // 1.2 体力计划参数与结果
        { "max_resin", I18nService.Key("体力用量（0 为全部）") }, { "craft_condensed_first", I18nService.Key("优先合成浓缩树脂") },
        { "event_priority", I18nService.Key("优先满足活动") }, { "event_strict_resin", I18nService.Key("严格使用 60 树脂") },
        { "dire_prestige_target", I18nService.Key("危战勋望目标（0 为不限制）") },
        { "domain", I18nService.Key("自动秘境") }, { "boss", I18nService.Key("自动首领讨伐") },
        { "stygian", I18nService.Key("自动幽境危战") }, { "ley_line", I18nService.Key("自动地脉花") }, { "craft", I18nService.Key("合成浓缩树脂") },
        { "budget", I18nService.Key("体力预算") }, { "steps", I18nService.Key("执行步骤") },

        // 2. 战斗与结束检测子设置（一条龙设置卡片展开的战斗对象）
        { "team_names", I18nService.Key("战斗队伍") }, { "fight_finish_detect_enabled", I18nService.Key("检测战斗结束") },
        { "finish_detect_config", I18nService.Key("战斗结束检测") }, { "action_scheduler_by_cd", I18nService.Key("按冷却调度动作") },
        { "only_pick_elite_drops_mode", I18nService.Key("精英掉落拾取模式") }, { "pick_drops_after_fight_enabled", I18nService.Key("战斗后拾取掉落物") },
        { "pick_drops_after_fight_seconds", I18nService.Key("战斗后拾取时长（秒）") }, { "fast_check_enabled", I18nService.Key("快速结束检测") },
        { "rotate_find_enemy_enabled", I18nService.Key("旋转寻找敌人") }, { "fast_check_params", I18nService.Key("快速检测参数") },
        { "check_after_switch_avatar", I18nService.Key("切换角色后检测") }, { "check_end_delay", I18nService.Key("结束检测延时") },
        { "before_detect_delay", I18nService.Key("检测前延时") }, { "rotary_factor", I18nService.Key("旋转幅度") },
        { "is_first_check", I18nService.Key("首次检测") }, { "check_before_burst", I18nService.Key("施放元素爆发前检测") },
        { "skip_fight_end_check_when_enemy_visible", I18nService.Key("敌人可见时跳过结束检测") },
        { "block_check_before_battle_seconds", I18nService.Key("战斗开始后暂缓检测（秒）") },
        { "paimon_end_check_enabled", I18nService.Key("派蒙图标辅助结束检测") }, { "paimon_end_check_delay", I18nService.Key("派蒙检测延时（秒）") },
        { "battle_threshold_for_loot", I18nService.Key("拾取前战斗次数") }, { "kazuha_pickup_enabled", I18nService.Key("万叶辅助拾取") },
        { "qin_double_pick_up", I18nService.Key("琴二次拾取") }, { "guardian_avatar", I18nService.Key("保护角色") },
        { "guardian_combat_skip", I18nService.Key("保护角色跳过战斗") }, { "guardian_avatar_hold", I18nService.Key("保护角色长按技能") },
        { "burst_enabled", I18nService.Key("使用元素爆发") }, { "kazuha_party_name", I18nService.Key("万叶拾取队伍") },
        { "swimming_enabled", I18nService.Key("游泳辅助") }, { "exp_based_pickup_enabled", I18nService.Key("根据经验识别拾取") },
        { "enable_combat_targeting", I18nService.Key("战斗目标锁定") }, { "lock_lost_wait_time", I18nService.Key("目标丢失等待（秒）") },
        { "seek_enemy_enabled", I18nService.Key("旋转寻找敌人") }, { "seek_enemy_interval_seconds", I18nService.Key("寻敌间隔（秒）") },
        { "seek_enemy_rotary_factor", I18nService.Key("寻敌旋转幅度") }, { "targeting_detection_interval", I18nService.Key("索敌识别间隔（毫秒）") },
        { "damage_number_recognition_mode", I18nService.Key("伤害数字识别方式") }, { "draw_recognition_results", I18nService.Key("显示识别结果边框") },

        // 3. 地图追踪、JS 与录制回放的宿主设置
        { "settings", I18nService.Key("JS 脚本设置") }, { "party_name", I18nService.Key("队伍名称") }, { "skip_party_switch", I18nService.Key("跳过队伍切换") },
        { "auto_pick_enabled", I18nService.Key("自动拾取") }, { "auto_fight_enabled", I18nService.Key("自动战斗") }, { "auto_skip_enabled", I18nService.Key("自动跳过剧情") },
        { "with_delay", I18nService.Key("按录制延时回放") }, { "country", I18nService.Key("国家") },

        // 4. Shell 与进程内 C# 参数
        { "file_name", I18nService.Key("程序或命令") }, { "arguments", I18nService.Key("参数列表") }, { "working_directory", I18nService.Key("工作目录") },
        { "operation", I18nService.Key("C# 操作名") }, { "values", I18nService.Key("计算输入") }, { "delay_milliseconds", I18nService.Key("延时（毫秒）") },
        { "units", I18nService.Key("额度单位") }, { "event_key", I18nService.Key("事件键") }, { "occurred_at", I18nService.Key("事件发生时间") },
        { "report_twice", I18nService.Key("重复报告事件（验收用）") },

        // 5. 运行记录与路线 CD 结果字段（时间字段按本机时间展示）
        { "resource_version", I18nService.Key("执行资源版本") }, { "run_id", I18nService.Key("运行 ID") },
        { "route_cd", I18nService.Key("路线 CD 配置") }, { "route_material", I18nService.Key("识别的材料或类别") },
        { "route_rule", I18nService.Key("本次 CD 规则") }, { "route_rule_source", I18nService.Key("规则依据") },
        { "route_last_success_at", I18nService.Key("上次成功时间") }, { "route_next_eligible_at", I18nService.Key("下次可执行时间") },
        { "route_basis", I18nService.Key("采集完成依据") }
    };

    /// <summary>
    /// 返回字段按当前界面语言翻译后的显示名；未登记的字段原样返回传入的字段名。
    /// </summary>
    /// <param name="key">蛇形字段名；调用方负责先把驼峰或帕斯卡命名转换为蛇形。</param>
    public static string GetDisplayName(string key) => Names.TryGetValue(key, out var name) ? I18nService.Instance.Translate(name) : key;
}
