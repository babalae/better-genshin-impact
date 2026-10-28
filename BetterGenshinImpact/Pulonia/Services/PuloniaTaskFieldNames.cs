using System;
using System.Collections.Generic;

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
        { "resin_usage", "树脂使用" },
        { "domain_name", "秘境" }, { "boss_name", "首领" },
        { "strategy_name", "战斗策略名称" }, { "combat_strategy_path", "战斗策略" },
        { "team_name", "队伍名称" }, { "team", "队伍" }, { "fight_team_name", "队伍" },
        { "revive_retry_count", "死亡重试次数" }, { "timeout", "战斗超时（秒）" },
        { "boss_num", "幽境首领序号" },
        { "one_dragon_mode", "地脉花一条龙模式" }, { "max_artifact_star", "分解最大星级" },
        { "auto_artifact_salvage", "结束后分解圣遗物" }, { "reward_recognition_enabled", "识别奖励" },
        { "sunday_selected_value", "周日奖励序号" }, { "ley_line_outcrop_type", "地脉花类型" },
        { "fight_config", "战斗设置" }, { "return_to_statue_after_each_round", "每轮结束回神像" },
        { "friendship_team", "好感队伍" },
        { "scan_drops_after_reward_enabled", "领奖后扫描掉落物" },
        { "scan_drops_after_reward_seconds", "扫描掉落物时长（秒）" }, { "fight_end_delay", "战斗结束后等待（秒）" },
        { "short_movement", "短距离移动" }, { "walk_to_f", "走近交互" }, { "left_right_move_times", "左右移动次数" },
        { "auto_eat", "自动吃药" }, { "min_resin_to_keep", "合成后保留原粹树脂" },
        { "teleport_type", "进壶方式" }, { "secret_treasure_objects", "洞天百宝购买" },
        { "wood_round_num", "伐木轮数" }, { "wood_daily_max_count", "每日伐木上限" },
        { "whole_process_timeout_seconds", "钓鱼流程超时（秒）" }, { "throw_rod_time_out_timeout_seconds", "抛竿超时（秒）" },
        { "fishing_time_policy", "钓鱼时段" }, { "save_screenshot_on_key_tick", "保存钓鱼关键步骤截图" },
        { "star", "圣遗物最高星级" }, { "java_script", "分解筛选脚本" }, { "artifact_set_filter", "圣遗物套装筛选" },
        { "max_num_to_check", "最多检查数量" }, { "recognition_failure_policy", "识别失败时的处理方式" },
        { "codes", "兑换码列表" }, { "target_items", "材料目标" }, { "craft_count", "指定合成份数" },

        // 1.1 自动秘境的提升指南设置
        { "training_guide_calculate_runs_enabled", "按养成计划计算次数" },
        { "training_guide_run_preference", "养成刷取偏好" },
        { "training_guide_crafting_bonus_reserve_percent", "合成收益预留（%）" },
        { "training_guide_reward_recognition_enabled", "按领奖结果更新养成库存" },
        { "training_guide_reward_failure_budget_enabled", "奖励识别失败时按固定预算继续" },
        { "training_guide_diagnostics_enabled", "保存养成诊断记录" },
        { "training_guide_low_ar_adaptation_enabled", "低冒险等阶适配" },
        { "training_guide_fallback_domain_name", "备选秘境" },
        { "training_guide_fallback_sunday_selected_value", "备选秘境周日奖励序号" },
        { "training_targets_json", "自定义培养目标" },

        // 1.2 体力计划参数与结果
        { "max_resin", "体力用量（0 为全部）" }, { "craft_condensed_first", "优先合成浓缩树脂" },
        { "domain", "自动秘境" }, { "boss", "自动首领讨伐" },
        { "stygian", "自动幽境危战" }, { "ley_line", "自动地脉花" }, { "craft", "合成浓缩树脂" },
        { "budget", "体力预算" }, { "steps", "执行步骤" },

        // 2. 战斗与结束检测子设置（一条龙设置卡片展开的战斗对象）
        { "team_names", "战斗队伍" }, { "fight_finish_detect_enabled", "检测战斗结束" },
        { "finish_detect_config", "战斗结束检测" }, { "action_scheduler_by_cd", "按冷却调度动作" },
        { "only_pick_elite_drops_mode", "精英掉落拾取模式" }, { "pick_drops_after_fight_enabled", "战斗后拾取掉落物" },
        { "pick_drops_after_fight_seconds", "战斗后拾取时长（秒）" }, { "fast_check_enabled", "快速结束检测" },
        { "rotate_find_enemy_enabled", "旋转寻找敌人" }, { "fast_check_params", "快速检测参数" },
        { "check_after_switch_avatar", "切换角色后检测" }, { "check_end_delay", "结束检测延时" },
        { "before_detect_delay", "检测前延时" }, { "rotary_factor", "旋转幅度" },
        { "is_first_check", "首次检测" }, { "check_before_burst", "施放元素爆发前检测" },
        { "skip_fight_end_check_when_enemy_visible", "敌人可见时跳过结束检测" },
        { "block_check_before_battle_seconds", "战斗开始后暂缓检测（秒）" },
        { "paimon_end_check_enabled", "派蒙图标辅助结束检测" }, { "paimon_end_check_delay", "派蒙检测延时（秒）" },
        { "battle_threshold_for_loot", "拾取前战斗次数" }, { "kazuha_pickup_enabled", "万叶辅助拾取" },
        { "qin_double_pick_up", "琴二次拾取" }, { "guardian_avatar", "保护角色" },
        { "guardian_combat_skip", "保护角色跳过战斗" }, { "guardian_avatar_hold", "保护角色长按技能" },
        { "burst_enabled", "使用元素爆发" }, { "kazuha_party_name", "万叶拾取队伍" },
        { "swimming_enabled", "游泳辅助" }, { "exp_based_pickup_enabled", "根据经验识别拾取" },
        { "enable_combat_targeting", "战斗目标锁定" }, { "lock_lost_wait_time", "目标丢失等待（秒）" },
        { "seek_enemy_enabled", "旋转寻找敌人" }, { "seek_enemy_interval_seconds", "寻敌间隔（秒）" },
        { "seek_enemy_rotary_factor", "寻敌旋转幅度" }, { "targeting_detection_interval", "索敌识别间隔（毫秒）" },
        { "damage_number_recognition_mode", "伤害数字识别方式" }, { "draw_recognition_results", "显示识别结果边框" },

        // 3. 地图追踪、JS 与录制回放的宿主设置
        { "settings", "JS 脚本设置" }, { "party_name", "队伍名称" }, { "skip_party_switch", "跳过队伍切换" },
        { "auto_pick_enabled", "自动拾取" }, { "auto_fight_enabled", "自动战斗" }, { "auto_skip_enabled", "自动跳过剧情" },
        { "with_delay", "按录制延时回放" }, { "country", "国家" },

        // 4. Shell 与进程内 C# 参数
        { "file_name", "程序或命令" }, { "arguments", "参数列表" }, { "working_directory", "工作目录" },
        { "operation", "C# 操作名" }, { "values", "计算输入" }, { "delay_milliseconds", "延时（毫秒）" },
        { "units", "额度单位" }, { "event_key", "事件键" }, { "occurred_at", "事件发生时间" },
        { "report_twice", "重复报告事件（验收用）" },

        // 5. 运行记录与路线 CD 结果字段（时间字段按本机时间展示）
        { "resource_version", "执行资源版本" }, { "run_id", "运行 ID" },
        { "collection_cd", "路线 CD 配置" }, { "collection_material", "识别的材料或类别" },
        { "collection_rule", "本次 CD 规则" }, { "collection_rule_source", "规则依据" },
        { "collection_last_success_at", "上次成功时间" }, { "collection_next_eligible_at", "下次可执行时间" },
        { "collection_basis", "采集完成依据" }
    };

    /// <summary>
    /// 返回字段的中文显示名；未登记的字段原样返回传入的字段名。
    /// </summary>
    /// <param name="key">蛇形字段名；调用方负责先把驼峰或帕斯卡命名转换为蛇形。</param>
    public static string GetDisplayName(string key) => Names.TryGetValue(key, out var name) ? name : key;
}
