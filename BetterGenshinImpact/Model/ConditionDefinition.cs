using BetterGenshinImpact.Helpers;
﻿using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFight.Config;

namespace BetterGenshinImpact.Model;

public class ConditionDefinition
{
    public string? Subject { get; set; } // 选项主体

    public string? Description { get; set; } // 选项描述

    // 谓语选项
    public List<string> PredicateOptions { get; set; } = [Lang.S["Gen_11924_e13556"]];

    public IEnumerable<string>? ObjectOptions { get; set; } // 宾语选项

    public List<string>? ResultOptions { get; set; } // 结果选项 为空使用文本框
}

// 条件定义
public class ConditionDefinitions
{
    public static Dictionary<string, ConditionDefinition> Definitions { get; set; } = new()
    {
        {
            Lang.S["Gen_10023_15d7ae"], new ConditionDefinition
            {
                Subject = Lang.S["Gen_10023_15d7ae"],
                Description = Lang.S["Gen_12014_3d3d6e"],
                ObjectOptions = new List<string> { Lang.S["Gen_10024_a8b0c2"],
                    Lang.S["Gen_12013_20edf4"],
                    Lang.S["Gen_12012_40c9d8"],
                    Lang.S["Gen_12011_fadfa0"],
                    Lang.S["Gen_12010_191c96"],
                    Lang.S["Gen_12009_3ee46d"],
                    Lang.S["Gen_12008_d68101"],
                    Lang.S["Gen_12007_8bc0e4"],
                    Lang.S["Gen_12006_ec1a6e"],
                    Lang.S["Gen_12005_86ef46"],
                    Lang.S["Gen_12004_908d6b"],
                    Lang.S["Gen_12003_249299"],
                    Lang.S["Gen_12002_90da87"],
                    Lang.S["Gen_12001_000041"],
                    Lang.S["Gen_12000_ea8588"],
                    Lang.S["Gen_11999_b86665"],
                    Lang.S["Gen_11998_c7d804"],
                    Lang.S["Gen_11997_7427f0"],
                    Lang.S["Gen_11996_0bd233"],
                    Lang.S["Gen_11995_1832ee"],
                    Lang.S["Gen_11994_1762a8"],
                    Lang.S["Gen_11993_fd57b4"],
                    Lang.S["Gen_11992_7e9516"],
                    Lang.S["Gen_11991_a23a8c"],
                    Lang.S["Gen_11990_ecd410"],
                    Lang.S["Gen_11989_7b603f"],
                    Lang.S["Gen_11988_9f7870"],
                    Lang.S["Gen_11987_4d7bd8"],
                    Lang.S["Gen_11986_3f27c8"],
                    Lang.S["Gen_11985_01a6e3"],
                    Lang.S["Gen_11984_a172ac"],
                    Lang.S["Gen_11983_40e543"],
                    Lang.S["Gen_11982_dd1714"],
                    Lang.S["Gen_11981_278b15"],
                    Lang.S["Gen_11980_8ff4c1"],
                    Lang.S["Gen_11979_23b9bc"],
                    Lang.S["Gen_11978_10fbcb"],
                    Lang.S["Gen_11977_f1eae9"],
                    Lang.S["Gen_11976_273f7b"],
                    Lang.S["Gen_11975_6a46ed"],
                    Lang.S["Gen_11974_87ad72"],
                    Lang.S["Gen_11973_7977e0"],
                    Lang.S["Gen_11972_933730"],
                    Lang.S["Gen_11972_933730"],
                    Lang.S["Gen_11971_154fec"],
                    Lang.S["Gen_11970_2008bd"],
                    Lang.S["Gen_11969_e21c92"],
                    Lang.S["Gen_11968_8d9150"],
                    Lang.S["Gen_11967_a14dce"],
                    Lang.S["Gen_11966_58837f"],
                    Lang.S["Gen_11965_684aa2"],
                    Lang.S["Gen_11964_0c72f3"],
                    Lang.S["Gen_11963_e7fb11"],
                    Lang.S["Gen_11962_1321ba"],
                    Lang.S["Gen_11961_9c7313"],
                    Lang.S["Gen_11960_37bc1f"],
                    Lang.S["Gen_11959_1fa9c9"],
                    Lang.S["Gen_11958_2d5fcb"],
                    Lang.S["Gen_11957_f62e9f"],
                    Lang.S["Gen_11956_17f79e"],
                    Lang.S["Gen_11955_1be69a"],
                    Lang.S["Gen_11954_e9b6f7"],
                    Lang.S["Gen_11953_8f1e87"],
                    Lang.S["Gen_11952_93c94a"],
                    Lang.S["Gen_11951_6966e3"],
                    Lang.S["Gen_11950_28a75d"],
                    Lang.S["Gen_11949_13ce55"],
                    Lang.S["Gen_11948_f12098"],
                    Lang.S["Gen_11947_0ff7f8"],
                    Lang.S["Gen_11946_c88aef"],
                    Lang.S["Gen_11945_0bb003"],
                    Lang.S["Gen_11944_932ab9"],
                    Lang.S["Gen_11943_66e743"],
                    Lang.S["Gen_11942_d9b934"],
                    Lang.S["Gen_11941_710422"],
                    Lang.S["Gen_11940_595691"],
                    Lang.S["Gen_11939_f28e1f"],
                    Lang.S["Gen_11938_274b5c"],
                    Lang.S["Gen_11937_f41eb3"],
                    Lang.S["Gen_11936_2be531"],
                    Lang.S["Gen_11935_cde1e6"],
                    Lang.S["Gen_11934_54ab59"],
                    Lang.S["Gen_11933_8d4c23"],
                    Lang.S["Gen_11932_008e6f"] },
            }
        },
        {
            Lang.S["KeyBind_002_c500cf"], new ConditionDefinition
            {
                Subject = Lang.S["KeyBind_002_c500cf"],
                Description = Lang.S["Gen_11931_fca6d0"],
                ObjectOptions = new List<string> { Lang.S["Gen_11929_343418"], "水元素采集", "雷元素采集", "风元素采集", "火元素采集" },
            }
        },
        {
            Lang.S["Gen_10026_37033c"], new ConditionDefinition
            {
                Subject = Lang.S["Gen_10026_37033c"],
                Description = Lang.S["Gen_11930_edd66b"],
                ObjectOptions = DefaultAutoFightConfig.CombatAvatarNames,
                ResultOptions = AvatarResultList
            }
        },
    };

    public static List<string> PartySubjects { get; } = [Lang.S["Gen_10023_15d7ae"], "动作"];

    public static List<string> AvatarSubjects { get; } = [Lang.S["Gen_10026_37033c"]];

    public static List<string> AvatarResultList { get; } = [Lang.S["Gen_10022_37be95"], "循环长E", "作为主要行走角色"];

    public static Dictionary<string, string> ActionCnDic { get; } = new()
    {
        { "nahida_collect", Lang.S["Gen_11929_343418"] },
        { "hydro_collect", Lang.S["Gen_11928_52008c"] },
        { "electro_collect", Lang.S["Gen_11927_104220"] },
        { "anemo_collect", Lang.S["Gen_11926_cc749b"] },
        { "pyro_collect", Lang.S["Gen_11925_736b93"] },
    };
}
