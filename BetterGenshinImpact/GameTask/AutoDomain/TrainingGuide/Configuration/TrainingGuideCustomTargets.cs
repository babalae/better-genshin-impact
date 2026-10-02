using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>本次运行的目标库存；不写入持久化设置。</summary>
public static class TrainingGuideCustomTargets
{
    public static Dictionary<TrainingGuideMaterial, int>? Parse(string? json)
    {
        if (json == null) return null;
        var rows = JArray.Parse(json);
        if (rows.Count == 0) throw new ArgumentException("自定义培养目标不能为空数组");
        var result = new Dictionary<TrainingGuideMaterial, int>();
        foreach (var row in rows)
        {
            if (row is not JObject obj || obj["material"]?.Type != JTokenType.String ||
                obj["target"]?.Type != JTokenType.Integer || obj.Properties().Any(p => p.Name is not ("material" or "target")))
                throw new ArgumentException("目标仅支持 material（完整材料名称）和 target（正整数目标库存）");
            var material = TrainingGuideMaterialCatalog.Find((string)obj["material"]!)
                ?? throw new ArgumentException($"未知培养材料：{obj["material"]}");
            var target = obj["target"]!.Value<long>();
            if (target <= 0 || target > int.MaxValue) throw new ArgumentException("目标库存必须为有效正整数");
            if (!result.TryAdd(material, (int)target)) throw new ArgumentException($"重复培养目标：{material.Name}");
            if (!TrainingGuideEntryCatalog.Entries.Any(e => e.Family == material.Family && e.IsWeapon == material.IsWeapon))
                throw new ArgumentException($"材料没有对应秘境入口：{material.Name}");
        }
        return result;
    }
}
