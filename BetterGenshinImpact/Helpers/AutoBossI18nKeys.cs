using BetterGenshinImpact.Service.I18n;
using System.Collections.Generic;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 首领名 i18n Key 登记。名称来自 AutoBossData.CountryToBosses，同步工具只扫描 I18nService.Key 字面量，
/// 在此显式登记；新增首领时需同步补充。首领名同时是保存值，只在 <see cref="AutoBossCascadingItems"/> 展示时翻译。
/// </summary>
internal static class AutoBossI18nKeys
{
    /// <summary>
    /// 首领名 Key。
    /// </summary>
    public static readonly IReadOnlyList<string> BossNames =
    [
        I18nService.Key("急冻树"),
        I18nService.Key("无相之雷"),
        I18nService.Key("守望者·堕天"),
        I18nService.Key("爆炎树"),
        I18nService.Key("纯水精灵"),
        I18nService.Key("古岩龙蜥"),
        I18nService.Key("无相之岩"),
        I18nService.Key("遗迹巨蛇"),
        I18nService.Key("隐山猊兽"),
        I18nService.Key("无相之火"),
        I18nService.Key("恒常机关阵列"),
        I18nService.Key("雷音权现"),
        I18nService.Key("魔偶剑鬼"),
        I18nService.Key("无相之水"),
        I18nService.Key("掣电树"),
        I18nService.Key("半永恒统辖矩阵"),
        I18nService.Key("翠翎恐蕈"),
        I18nService.Key("风蚀沙虫"),
        I18nService.Key("无相之草"),
        I18nService.Key("深罪浸礼者"),
        I18nService.Key("兆载永劫龙兽"),
        I18nService.Key("歌裴莉娅的葬送"),
        I18nService.Key("科培琉司的劫罚"),
        I18nService.Key("实验性场力发生装置"),
        I18nService.Key("魔像督军"),
        I18nService.Key("千年珍珠骏麟"),
        I18nService.Key("水形幻人"),
        I18nService.Key("铁甲熔火帝皇"),
        I18nService.Key("金焰绒翼龙暴君"),
        I18nService.Key("灵觉隐修的迷者"),
        I18nService.Key("秘源机兵·构型械"),
        I18nService.Key("秘源机兵·统御械"),
        I18nService.Key("熔岩辉龙像"),
        I18nService.Key("贪食匿叶龙山王"),
        I18nService.Key("蕴光月守宫"),
        I18nService.Key("深黯魇语之主"),
        I18nService.Key("超重型陆巡舰·机动战垒"),
        I18nService.Key("霜夜巡天灵主"),
        I18nService.Key("蕴光月幻蝶"),
        I18nService.Key("重拳出击鸭"),
        I18nService.Key("不灭衍生造物"),
        I18nService.Key("嵌合翼骏狮"),
        I18nService.Key("游雪的护刃"),
    ];
}
