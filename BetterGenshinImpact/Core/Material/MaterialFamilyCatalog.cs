using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Material;

/// <summary>读取独立 JSON 家族表，所有业务使用同一份显式升级关系。</summary>
public static class MaterialFamilyCatalog
{
    /// <summary>随程序嵌入的家族表；同一源文件同时复制到输出目录供其他任务使用。</summary>
    private const string ResourceName = "BetterGenshinImpact.Resources.Material.material-families.json";

    /// <summary>按低阶到高阶排列的只读家族目录。</summary>
    public static IReadOnlyList<MaterialFamily> Families { get; } = Load();

    /// <summary>一次读取并校验目录，不依赖用户账号、库存或游戏任务。</summary>
    private static IReadOnlyList<MaterialFamily> Load()
    {
        using var stream = typeof(MaterialFamilyCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("缺少材料家族表。");
        using var reader = new StreamReader(stream);
        var document = JObject.Parse(reader.ReadToEnd());
        if ((int?)document["schemaVersion"] != 1 || document["families"] is not JArray entries)
            throw new InvalidDataException("材料家族表格式无效。");
        var families = entries.ToObject<MaterialFamily[]>() ?? throw new InvalidDataException("材料家族表为空。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var memberIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in families)
        {
            // 当前计算器仅支持单向 3:1，新增其他配方时必须同时更新计算规则。
            if (family == null || string.IsNullOrWhiteSpace(family.Id) || !ids.Add(family.Id) ||
                family.Members == null || family.MemberIds == null || family.MoraCosts == null ||
                family.Members.Count < 2 || family.MemberIds.Count != family.Members.Count ||
                family.MoraCosts.Count != family.Members.Count - 1 || family.MoraCosts.Any(cost => cost < 0) ||
                family.InputCount != 3 || family.OutputCount != 1 ||
                family.Members.Any(name => string.IsNullOrWhiteSpace(name) || !names.Add(name)) ||
                family.MemberIds.Any(id => string.IsNullOrWhiteSpace(id) || !memberIds.Add(id)))
                throw new InvalidDataException("材料家族表存在重复成员或无效升级配方。");
        }
        // 不向业务调用方暴露反序列化得到的可变集合。
        return Array.AsReadOnly(families.Select(family => family with
        {
            Members = Array.AsReadOnly(family.Members.ToArray()),
            MemberIds = Array.AsReadOnly(family.MemberIds.ToArray()),
            MoraCosts = Array.AsReadOnly(family.MoraCosts.ToArray())
        }).ToArray());
    }
}