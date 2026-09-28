using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public sealed record TrainingGuideEntry(string Domain, string Entry, string Family, bool IsWeapon);

/// <summary>入口名称决定家族；最高难度的材料图标从高等级到低等级排列。</summary>
public static class TrainingGuideEntryCatalog
{
    // 2026-09-28 核对：BWIKI 武器天赋素材（各材料的秘境来源）。
    // https://wiki.biligame.com/ys/武器天赋素材
    // 每个秘境按一四、二五、三六顺序列出，仅供维护阅读，不按数组位置推断入口。
    public static IReadOnlyList<TrainingGuideEntry> Entries { get; } = Array.AsReadOnly<TrainingGuideEntry>([
        new("塞西莉亚苗圃", "炼武秘境：水光之城", "高塔孤王", true),
        new("塞西莉亚苗圃", "炼武秘境：深没之谷", "凛风奔狼", true),
        new("塞西莉亚苗圃", "炼武秘境：渴水的废都", "狮牙斗士", true),
        new("震雷连山密宫", "炼武秘境：雷云祭坛", "孤云寒林", true),
        new("震雷连山密宫", "炼武秘境：鸣雷城墟", "雾海云间", true),
        new("震雷连山密宫", "炼武秘境：古雷试炼场", "漆黑陨铁", true),
        new("砂流之庭", "炼武秘境：沉沙之渊", "远海夷地", true),
        new("砂流之庭", "炼武秘境：砂之祭场", "鸣神御灵", true),
        new("砂流之庭", "炼武秘境：流沙之葬", "今昔剧画", true),
        new("有顶塔", "炼武秘境：云垢", "谧林涓露", true),
        new("有顶塔", "炼武秘境：思惑", "绿洲花园", true),
        new("有顶塔", "炼武秘境：引业", "烈日威权", true),
        new("深潮的余响", "炼武秘境：机思", "悠古弦音", true),
        new("深潮的余响", "炼武秘境：匠理", "纯圣露滴", true),
        new("深潮的余响", "炼武秘境：奇械", "无垢之海", true),
        new("深古瞭望所", "炼武秘境：冥见", "贡祭炽心", true),
        new("深古瞭望所", "炼武秘境：究观", "谵妄圣主", true),
        new("深古瞭望所", "炼武秘境：测度", "神合秘烟", true),
        new("失落的月庭", "炼武秘境：明辉", "奇巧秘器", true),
        new("失落的月庭", "炼武秘境：祷颂", "长夜燧火", true),
        new("失落的月庭", "炼武秘境：祭月", "终北遗嗣", true),
        new("妄念的创痕", "炼武秘境：铸铁", "苍星军势", true),
        new("妄念的创痕", "炼武秘境：链轨", "藏窖灵浆", true),
        new("妄念的创痕", "炼武秘境：断钢", "凛雪帝皇", true),
        new("忘却之峡", "精通秘境：霜凝祭坛", "「自由」", false),
        new("忘却之峡", "精通秘境：冰封废渊", "「抗争」", false),
        new("忘却之峡", "精通秘境：沉睡之国", "「诗文」", false),
        new("太山府", "精通秘境：炽炎祭场", "「繁荣」", false),
        new("太山府", "精通秘境：深炎之底", "「勤劳」", false),
        new("太山府", "精通秘境：焚尽之环", "「黄金」", false),
        new("菫色之庭", "精通秘境：菫染之国", "「浮世」", false),
        new("菫色之庭", "精通秘境：初雷幽谷", "「风雅」", false),
        new("菫色之庭", "精通秘境：真葛废都", "「天光」", false),
        new("昏识塔", "精通秘境：圆镜", "「诤言」", false),
        new("昏识塔", "精通秘境：妙语", "「巧思」", false),
        new("昏识塔", "精通秘境：律藏", "「笃行」", false),
        new("苍白的遗荣", "精通秘境：旋韵", "「公平」", false),
        new("苍白的遗荣", "精通秘境：箴铭", "「正义」", false),
        new("苍白的遗荣", "精通秘境：琅诵", "「秩序」", false),
        new("蕴火的幽墟", "精通秘境：转竟", "「角逐」", false),
        new("蕴火的幽墟", "精通秘境：空华", "「焚燔」", false),
        new("蕴火的幽墟", "精通秘境：旋复", "「纷争」", false),
        new("无光的深都", "精通秘境：墟都", "「月光」", false),
        new("无光的深都", "精通秘境：遗荫", "「乐园」", false),
        new("无光的深都", "精通秘境：覆巢", "「浪迹」", false),
        new("荒坠的圣迹", "精通秘境：默想", "「慈爱」", false),
        new("荒坠的圣迹", "精通秘境：隐修", "「坚忍」", false),
        new("荒坠的圣迹", "精通秘境：共观", "「荣光」", false),
    ]);

    public static string NormalizeEntry(string text)
    {
        var normalized = TrainingGuideMaterialCatalog.Normalize(text).Replace(':', '：')
            .Replace("沉砂之渊", "沉沙之渊");
        normalized = Regex.Replace(normalized, @"[（(].*$", "");
        return Regex.Replace(normalized, @"[IVXⅠⅡⅢⅣⅤⅥ]+$", "", RegexOptions.IgnoreCase);
    }

    public static TrainingGuideEntry? Find(string domain, string entry) =>
        Entries.SingleOrDefault(e =>
            TrainingGuideMaterialCatalog.Normalize(e.Domain) == TrainingGuideMaterialCatalog.Normalize(domain) &&
            NormalizeEntry(e.Entry) == NormalizeEntry(entry));
}

