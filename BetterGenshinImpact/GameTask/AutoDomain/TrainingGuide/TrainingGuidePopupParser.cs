using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public sealed record TrainingGuideMaterialReading(
    TrainingGuideMaterial Material, int Stock, int Required, bool IsTarget);

/// <summary>只解析局部 OCR 的库存和培养目标。缺失数字不等于零。</summary>
public static class TrainingGuidePopupParser
{
    /// <summary>材料身份由图标确认；只接受明确的库存/培养标签，不依赖描述、来源或可合成数量。</summary>
    public static TrainingGuideMaterialReading? ParseQuantities(TrainingGuideMaterial material, string footer)
    {
        var lines = footer.Replace('／', '/').Split('\n');
        TrainingGuideMaterialReading? result = null;
        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw, @"\s+", "");
            var demand = Regex.Match(line, @"^培养需求[:：]?([0-9]+)/([0-9]+)$");
            var inventory = Regex.Match(line, @"^当前拥有[:：]?([0-9]+)$");
            if (!demand.Success && !inventory.Success)
            {
                if (line.Contains("培养需求") || line.Contains("当前拥有")) return null;
                continue;
            }
            if (result != null) return null;
            if (!int.TryParse((demand.Success ? demand : inventory).Groups[1].Value, out var stock)) return null;
            var required = 0;
            if (demand.Success && (!int.TryParse(demand.Groups[2].Value, out required) || required <= 0)) return null;
            result = new(material, stock, required, demand.Success);
        }
        return result;
    }
}
