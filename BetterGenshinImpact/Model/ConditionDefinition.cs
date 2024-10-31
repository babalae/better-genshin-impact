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

    public List<string>? ObjectOptions { get; set; } // 宾语选项

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
                Description = "采集物使用的队伍，建议勾选全部并配置存在盾、加血的角色",
                ObjectOptions = new List<string> { "全部", "矿石", "特殊" },
            }
        },
        {
            "动作", new ConditionDefinition
            {
                Subject = "动作",
                Description = "你所配置的队伍中，必须存在和动作相关的角色。程序会自动识别并使用对应的角色执行动作，具体见文档",
                ObjectOptions = new List<string> { "纳西妲采集", "水元素采集", "雷元素采集", "风元素采集" },
            }
        },
        {
            "队伍中角色", new ConditionDefinition
            {
                Subject = "队伍中角色",
                Description = "队伍中存在配置角色时候，该角色循环执行的策略。配置多个角色时优先级从左到右。\n具体策略产生的行为见文档。",
                ObjectOptions = DefaultAutoFightConfig.CombatAvatarNames,
                ResultOptions = ["循环短E", "循环长E","作为主要行走人员"]
            }
        },
    };

    public static List<string> Subjects { get; set; } = Definitions.Keys.ToList();
}
