using BetterGenshinImpact.Service.I18n;
using System.Collections.Generic;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 七天神像区域名 i18n Key 登记。区域名来自 tp.json 中七天神像的一级区域（MapLazyAssets.GoddessPositions），
/// 同步工具只扫描 I18nService.Key 字面量，在此显式登记；新增七天神像区域时需同步补充。
/// 区域名同时是保存值（TpConfig.ReviveStatueOfTheSevenArea），只在通用设置页下拉框展示时翻译。国家名已在其他位置登记。
/// </summary>
internal static class StatueOfTheSevenI18nKeys
{
    /// <summary>
    /// 七天神像一级区域名 Key，按国家分组排列。
    /// </summary>
    public static readonly IReadOnlyList<string> AreaNames =
    [
        // 蒙德
        I18nService.Key("苍风高地"),
        I18nService.Key("风啸山坡"),
        I18nService.Key("坠星山谷"),
        I18nService.Key("明冠山地"),
        I18nService.Key("龙脊雪山"),
        I18nService.Key("风息山"),
        // 璃月
        I18nService.Key("碧水原"),
        I18nService.Key("云来海"),
        I18nService.Key("珉林"),
        I18nService.Key("璃沙郊"),
        I18nService.Key("琼玑野"),
        I18nService.Key("层岩巨渊"),
        I18nService.Key("沉玉谷·上谷"),
        I18nService.Key("沉玉谷·南陵"),
        // 稻妻
        I18nService.Key("鸣神岛"),
        I18nService.Key("神无冢"),
        I18nService.Key("八酝岛"),
        I18nService.Key("清籁岛"),
        I18nService.Key("鹤观"),
        I18nService.Key("海祇岛"),
        // 须弥
        I18nService.Key("护世森"),
        I18nService.Key("阿陀河谷"),
        I18nService.Key("桓那兰那"),
        I18nService.Key("善见地"),
        I18nService.Key("道成林"),
        I18nService.Key("失落的苗圃"),
        I18nService.Key("下风蚀地"),
        I18nService.Key("列柱沙原"),
        I18nService.Key("上风蚀地"),
        I18nService.Key("二净甸"),
        I18nService.Key("千壑沙地"),
        I18nService.Key("荒石苍漠"),
        I18nService.Key("浮罗囿"),
        // 枫丹
        I18nService.Key("白露区"),
        I18nService.Key("苍晶区"),
        I18nService.Key("枫丹廷区"),
        I18nService.Key("黎翡区"),
        I18nService.Key("枫丹动能工程科学研究院区"),
        I18nService.Key("伊黎耶林区"),
        I18nService.Key("莫尔泰区"),
        I18nService.Key("诺思托伊区"),
        // 纳塔
        I18nService.Key("坚岩隘谷"),
        I18nService.Key("万火之瓯"),
        I18nService.Key("涌流地"),
        I18nService.Key("踞石山"),
        I18nService.Key("镜璧山"),
        I18nService.Key("奥奇卡纳塔"),
        I18nService.Key("安饶之野"),
        I18nService.Key("悠悠度假村"),
        I18nService.Key("翘枝崖"),
        // 挪德卡莱
        I18nService.Key("帕哈岛"),
        I18nService.Key("伦波岛"),
        I18nService.Key("希汐岛"),
        I18nService.Key("虚海望"),
        I18nService.Key("逐浪野"),
        I18nService.Key("烟硌山峰"),
        I18nService.Key("杜南纳深坑"),
        // 至冬
        I18nService.Key("古兽冰原"),
        I18nService.Key("永凝冻土"),
        I18nService.Key("霜殛寒峰"),
        I18nService.Key("焰羽谷"),
        I18nService.Key("白桦雪葬地"),
    ];
}
