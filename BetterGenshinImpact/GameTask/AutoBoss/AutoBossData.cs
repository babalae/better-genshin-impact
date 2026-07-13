using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoBoss;

/// <summary>
/// 自动首领讨伐支持的 Boss 数据和路线类型分类。
/// </summary>
public static class AutoBossData
{
    private static IReadOnlyList<string>? _supportedBossNames;

    /// <summary>
    /// 按国家分组的可选 Boss 列表，UI 级联下拉只展示这里配置的目标。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CountryToBosses =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["蒙德"] = ["急冻树", "无相之雷", "守望者·堕天"],
            ["璃月"] = ["爆炎树", "纯水精灵", "古岩龙蜥", "无相之岩", "遗迹巨蛇", "隐山猊兽"],
            ["稻妻"] = ["无相之火", "恒常机关阵列", "雷音权现", "魔偶剑鬼", "无相之水"],
            ["须弥"] = ["掣电树", "半永恒统辖矩阵", "翠翎恐蕈", "风蚀沙虫", "无相之草", "深罪浸礼者", "兆载永劫龙兽"],
            ["枫丹"] = ["歌裴莉娅的葬送", "科培琉司的劫罚", "实验性场力发生装置", "魔像督军", "千年珍珠骏麟", "水形幻人", "铁甲熔火帝皇"],
            ["纳塔"] = ["金焰绒翼龙暴君", "灵觉隐修的迷者", "秘源机兵·构型械", "秘源机兵·统御械", "熔岩辉龙像", "深邃摹结株", "贪食匿叶龙山王"],
            ["挪德卡莱"] = ["蕴光月守宫", "深黯魇语之主", "超重型陆巡舰·机动战垒", "霜夜巡天灵主", "蕴光月幻蝶", "重拳出击鸭"]
        };

    /// <summary>
    /// 所有支持的 Boss 名称扁平列表。
    /// </summary>
    public static IReadOnlyList<string> SupportedBossNames =>
        _supportedBossNames ??= CountryToBosses.Values.SelectMany(x => x).ToList();

    /// <summary>
    /// 战斗后需要重新交互才能再次讨伐的 Boss。
    /// </summary>
    public static readonly HashSet<string> TalkToStartBosses =
    [
        "歌裴莉娅的葬送",
        "科培琉司的劫罚",
        "纯水精灵",
        "重拳出击鸭"
    ];

    /// <summary>
    /// 所在分层地图还不支持路径追踪的Boss
    /// 暂时使用强制传送和键鼠宏寻路。
    /// </summary>
    public static readonly HashSet<string> NoPathingSupportBosses =
    [
        "蕴光月守宫",
        "超重型陆巡舰·机动战垒",
        "蕴光月幻蝶"
    ];

    /// <summary>
    /// 判断 Boss 是否在支持列表中。
    /// </summary>
    /// <param name="bossName">Boss 名称。</param>
    /// <returns>支持该 Boss 时返回 true。</returns>
    public static bool IsSupportedBoss(string bossName)
    {
        return SupportedBossNames.Contains(bossName);
    }

    /// <summary>
    /// 判断 Boss 是否需要重新交互。
    /// </summary>
    /// <param name="bossName">Boss 名称。</param>
    /// <returns>需要执行战后快速前往路线时返回 true。</returns>
    public static bool IsTalkToStartBoss(string bossName)
    {
        return TalkToStartBosses.Contains(bossName);
    }

    /// <summary>
    /// 判断 Boss 是否不支持地图追踪。
    /// </summary>
    /// <param name="bossName">Boss 名称。</param>
    /// <returns>需要强制传送加键鼠宏路线时返回 true。</returns>
    public static bool IsNoPathingSupportBoss(string bossName)
    {
        return NoPathingSupportBosses.Contains(bossName);
    }
}
