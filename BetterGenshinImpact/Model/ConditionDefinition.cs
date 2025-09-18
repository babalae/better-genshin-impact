using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFight.Config;

namespace BetterGenshinImpact.Model;

public class ConditionDefinition
{
    public string? Subject { get; set; } // 选项主体

    public string? Description { get; set; } // 选项描述

    // 谓语选项
    public List<string> PredicateOptions { get; set; } = ["包含"];

    public IEnumerable<string>? ObjectOptions { get; set; } // 宾语选项

    public List<string>? ResultOptions { get; set; } // 结果选项 为空使用文本框
}

// 条件定义
public class ConditionDefinitions
{
    public static Dictionary<string, ConditionDefinition> Definitions { get; set; } = new()
    {
        {
            "采集物", new ConditionDefinition
            {
                Subject = "采集物",
                Description = "地图追踪此采集物时使用的队伍名称。队伍名称是你在游戏中手动设置的队伍名称文字。建议勾选全部并配置存在盾、加血的角色队伍。",
                ObjectOptions = new List<string> { "全部",
                    "万相石",
                    "初露之源",
                    "劫波莲",
                    "卷心菜",
                    "嘟嘟莲",
                    "圣金虫",
                    "堇瓜",
                    "塞西莉亚",
                    "墩墩桃",
                    "夜泊石",
                    "天云草实",
                    "子探测单元",
                    "小灯草",
                    "帕蒂莎兰",
                    "幽光星星",
                    "幽灯蕈",
                    "悼灵花",
                    "慕风蘑菇",
                    "日落果",
                    "星螺",
                    "星银矿石",
                    "晶化骨髓",
                    "月莲",
                    "松果",
                    "松茸",
                    "柔灯铃",
                    "树王圣体菇",
                    "树莓",
                    "椰枣",
                    "水晶块",
                    "沉玉仙茗",
                    "沙脂蛹",
                    "泡泡桔",
                    "浪沫羽鳃",
                    "海灵芝",
                    "海草",
                    "海露花",
                    "清心",
                    "清水玉",
                    "湖光铃兰",
                    "澄晶石",
                    "灼灼彩菊",
                    "灼灼彩菊",
                    "珊瑚真珠",
                    "琉璃百合",
                    "琉璃袋",
                    "甜甜花",
                    "电气水晶",
                    "白萝卜",
                    "白铁块",
                    "石珀",
                    "神秘的肉",
                    "竹笋",
                    "紫晶块",
                    "绝云椒椒",
                    "绯樱绣球",
                    "肉龙掌",
                    "胡萝卜",
                    "苍晶螺",
                    "苦种",
                    "茉洁草",
                    "莲蓬",
                    "萃凝晶",
                    "落落莓",
                    "蒲公英",
                    "薄荷",
                    "蘑菇",
                    "虹彩蔷薇",
                    "血斛",
                    "赤念果",
                    "金鱼草",
                    "钩钩果",
                    "铁块",
                    "霓裳花",
                    "青蜜莓",
                    "须弥蔷薇",
                    "风车菊",
                    "香辛果",
                    "马尾",
                    "鬼兜虫",
                    "魔晶块",
                    "鸟蛋",
                    "鸣草" },
            }
        },
        {
            "动作", new ConditionDefinition
            {
                Subject = "动作",
                Description = "路线中含有特殊动作时使用的队伍名称，优先级高于采集物配置。队伍名称是你在游戏中手动设置的队伍名称文字。队伍中，必须存在和动作相关的角色。程序会自动识别并使用对应的角色执行动作，具体见文档",
                ObjectOptions = new List<string> { "纳西妲采集", "水元素采集", "雷元素采集", "风元素采集", "火元素采集" },
            }
        },
        {
            "队伍中角色", new ConditionDefinition
            {
                Subject = "队伍中角色",
                Description = "队伍中存在配置角色时候，该角色循环执行的策略。配置多个角色时优先级从左到右。具体策略产生的行为见文档。",
                ObjectOptions = DefaultAutoFightConfig.CombatAvatarNames,
                ResultOptions = AvatarResultList
            }
        },
    };

    public static List<string> PartySubjects { get; } = ["采集物", "动作"];

    public static List<string> AvatarSubjects { get; } = ["队伍中角色"];

    public static List<string> AvatarResultList { get; } = ["循环短E", "循环长E", "作为主要行走角色"];

    public static Dictionary<string, string> ActionCnDic { get; } = new()
    {
        { "nahida_collect", "纳西妲采集" },
        { "hydro_collect", "水元素采集" },
        { "electro_collect", "雷元素采集" },
        { "anemo_collect", "风元素采集" },
        { "pyro_collect", "火元素采集" },
    };
}
