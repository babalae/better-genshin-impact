using System;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public sealed record TrainingGuideMaterialReading(
    TrainingGuideMaterial Material, int Stock, int Required, int? Craftable, bool IsTarget, string Source);

/// <summary>只解析局部 OCR 的四个字段。缺失数字不等于零。</summary>
public static class TrainingGuidePopupParser
{
    public static TrainingGuideMaterialReading? Parse(string title, string category, string footer, string source)
    {
        var material = TrainingGuideMaterialCatalog.Find(title);
        if (material == null) return null;
        category = TrainingGuideMaterialCatalog.Normalize(category);
        if (material.IsWeapon ? !category.Contains("武器突破素材") :
            !(category.Contains("天赋") && category.Contains("素材"))) return null;
        footer = Regex.Replace(footer.Replace('／', '/'), @"[^\S\r\n]+", "");
        var demands = Regex.Matches(footer, @"培养需求[:：]?\s*([0-9]+)\s*/\s*([0-9]+)(?=\s|$)");
        if (demands.Count > 1) return null;
        var demand = demands.Count == 1 ? demands[0] : Match.Empty;
        int stock, required;
        if (demand.Success)
        {
            if (!int.TryParse(demand.Groups[1].Value, out stock) ||
                !int.TryParse(demand.Groups[2].Value, out required)) return null;
        }
        else
        {
            // 未标记的低级材料仅接受明确库存标签，不猜测描述中的任意数字。
            if (footer.Contains("培养需求")) return null;
            var inventory = Regex.Match(footer, @"(?:^|\n)(?:当前)?(?:拥有|持有|库存)(?:数量)?[:：]?\s*([0-9]+)(?=\s|$)");
            if (!inventory.Success || !int.TryParse(inventory.Groups[1].Value, out stock)) return null;
            required = 0;
        }
        var craft = Regex.Match(footer, @"可合成数量[:：]?\s*([0-9]+)(?=\s|$)");
        int? craftable = null;
        if (craft.Success && int.TryParse(craft.Groups[1].Value, out var count)) craftable = count;
        else if (footer.Contains("可合成数量")) return null;
        return new(material, stock, required, craftable, demand.Success,
            TrainingGuideMaterialCatalog.Normalize(source));
    }
}
