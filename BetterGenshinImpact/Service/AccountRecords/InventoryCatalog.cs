using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BetterGenshinImpact.Core.Config;
using Microsoft.VisualBasic.FileIO;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>按标准名称加载公共目录；分类缺失不阻止数量上报。</summary>
public sealed class InventoryCatalog
{
    /// <summary>同一实例只读取一次 CSV，模型更新后随程序重启刷新。</summary>
    private readonly Lazy<Dictionary<string, string>> _items;

    /// <summary>使用现有资源包中的物品目录。</summary>
    public InventoryCatalog() : this(Global.Absolute(@"Assets\Model\ItemV2\item.csv")) { }

    /// <summary>允许验证时提供独立的目录文件。</summary>
    public InventoryCatalog(string path) => _items = new(() => Read(path));

    /// <summary>所有标准名称及分类；空字符串表示模型没有提供分类。</summary>
    public IReadOnlyDictionary<string, string> Items => _items.Value;

    /// <summary>解析 CSV，不载入不参与库存展示的向量数据。</summary>
    private static Dictionary<string, string> Read(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("物品字典缺少表头。");
        var nameIndex = Array.IndexOf(headers, "item_name");
        var categoryIndex = Array.IndexOf(headers, "material_type");
        if (nameIndex < 0 || categoryIndex < 0) throw new InvalidDataException("物品字典缺少名称或类型列。");
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields == null || fields.Length <= Math.Max(nameIndex, categoryIndex)) continue;
            var name = fields[nameIndex].Trim();
            if (name.Length == 0) continue;
            var category = fields[categoryIndex].Trim();
            if (!items.TryGetValue(name, out var current) || current.Length == 0) items[name] = category;
        }
        if (items.Count == 0) throw new InvalidDataException("物品字典为空，暂不能校验库存名称。");
        return items;
    }
}
