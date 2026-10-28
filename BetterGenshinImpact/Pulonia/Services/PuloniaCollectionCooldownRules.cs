using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 路线 CD 的集中规则表：材料名、别名与类别目录 → 刷新规则。
/// 数据来源为 Docs/technical/genshin-collectable-respawn.md，运行时不解析文档；目录名按官方脚本仓库实际结构维护。
/// </summary>
internal static class PuloniaCollectionCooldownRules
{
    /// <summary>
    /// 一档刷新规则。
    /// </summary>
    /// <param name="Mode">规则模式，不会是自动识别。</param>
    /// <param name="Hours">滚动小时数。</param>
    /// <param name="Days">日期间隔天数。</param>
    /// <param name="IsReference">文档标明数据不确定的参考规则。</param>
    internal readonly record struct Rule(PuloniaCollectionCooldownMode Mode, double Hours, int Days, bool IsReference);

    /// <summary>目录名 → 规则；精确匹配，不做模糊或包含匹配。</summary>
    private static readonly IReadOnlyDictionary<string, Rule> Rules = Create();

    /// <summary>
    /// 从文件所在目录向上逐级精确匹配，最近一级命中即停止；材料目录总在类别目录之下，因此材料规则优先。
    /// </summary>
    /// <param name="path">文件路径或文件身份；最后一段视为文件名，不参与匹配。</param>
    /// <param name="directoryName">命中的目录名。</param>
    /// <param name="rule">命中的规则。</param>
    /// <returns>是否命中。</returns>
    public static bool TryMatch(string path, out string directoryName, out Rule rule)
    {
        var names = path.Replace('\\', '/').Split('/');
        for (var i = names.Length - 2; i >= 0; i--)
        {
            if (!Rules.TryGetValue(names[i], out rule)) continue;
            directoryName = names[i];
            return true;
        }
        directoryName = string.Empty;
        rule = default;
        return false;
    }

    /// <summary>建立刷新文档中的明确材料、别名与类别目录；混合规则不猜测统一时长。</summary>
    private static Dictionary<string, Rule> Create()
    {
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        // 1. 特产与明确的植物、食材、生物：采集后滚动计时
        Add(PuloniaCollectionCooldownMode.Rolling, 46, 0, false, "钩钩果、落落莓、风车菊、塞西莉亚花、塞西莉亚、小灯草、慕风蘑菇、蒲公英籽、蒲公英、嘟嘟莲、空羽蛾、琉璃袋、绝云椒椒、石珀、霓裳花、琉璃百合、清心、星螺、夜泊石、清水玉、海灵芝、鸣草、鬼兜虫、晶化骨髓、血斛、绯樱绣球、天云草实、珊瑚真珠、幽灯蕈、月莲、劫波莲、帕蒂沙兰、帕蒂莎兰、树王圣体菇、圣金虫、赤念果、沙脂蛹、万相石、悼灵花、柔灯铃、虹彩蔷薇、苍晶螺、海露花、幽光星星、子探测单元、湖光铃兰、初露之源、青蜜莓、浪沫羽鳃、肉龙掌、灼灼彩菊、枯叶紫英、微光角菌、云岩裂叶、琉鳞石、霜盏花、便携轴承、月落银、冬凌草、松珀香、凝月泪滴、植绒草、金蕨、霜仙花");
        Add(PuloniaCollectionCooldownMode.Rolling, 46, 0, true, "冰雾花、冰雾花花朵、烈焰花、烈焰花花蕊、莲蓬");
        Add(PuloniaCollectionCooldownMode.Rolling, 12, 0, false, "甜甜花、薄荷、蝴蝶翅膀、树莓、野猪、鸟、鸭子、兽肉、禽肉、可捕捉的鱼、鱼肉");
        Add(PuloniaCollectionCooldownMode.Rolling, 24, 0, false, "蘑菇、松果、苹果、日落果、马尾、胡萝卜、白萝卜、小麦、琥珀、青蛙、螃蟹");
        Add(PuloniaCollectionCooldownMode.Rolling, 24, 0, true, "电气水晶、蜥蜴、蜥蜴尾巴、发光髓");
        // 官方仓库的钓鱼路线位于“其他/提瓦特钓鱼指南”
        Add(PuloniaCollectionCooldownMode.Rolling, 72, 0, false, "钓鱼、钓鱼点、钓鱼点鱼群、提瓦特钓鱼指南");
        // 2. 每日 04:00 重置：晶蝶与魔物
        Add(PuloniaCollectionCooldownMode.Daily, 0, 0, true, "晶核、晶蝶");
        Add(PuloniaCollectionCooldownMode.Daily, 0, 0, false, "魔物、怪物、普通怪、普通怪物、精英怪、精英怪物、地方传奇、地脉花、盗宝团");
        // 类别目录：官方仓库按“敌人与魔物/<怪物名>”“锄地专区/<路线组>”组织魔物路线，怪物名不在表中时由类别兜底。
        Add(PuloniaCollectionCooldownMode.Daily, 0, 0, false, "敌人与魔物、锄地专区");
        // 3. 矿石：按服务器日期估算；官方仓库目录名为“魔晶矿”
        Add(PuloniaCollectionCooldownMode.CalendarDays, 0, 1, false, "铁块");
        Add(PuloniaCollectionCooldownMode.CalendarDays, 0, 2, false, "白铁块、星银矿石");
        Add(PuloniaCollectionCooldownMode.CalendarDays, 0, 3, false, "水晶块、紫晶块、魔晶块、魔晶矿、萃凝晶、虹滴晶");
        // 4. 一次性与混合类别；混合类别识别后不检查 CD，与“未识别”区分显示
        Add(PuloniaCollectionCooldownMode.Once, 0, 0, false, "宝箱、岩之印调查");
        Add(PuloniaCollectionCooldownMode.Disabled, 0, 0, false, "圣遗物、圣遗物调查点、木材、木材伐木点、摩拉盒");
        return rules;

        // 同一档材料集中维护，别名也独立匹配，不把父目录整组作为采集记录。
        void Add(PuloniaCollectionCooldownMode mode, double hours, int days, bool reference, string names)
        {
            foreach (var name in names.Split('、')) rules.Add(name, new Rule(mode, hours, days, reference));
        }
    }
}
