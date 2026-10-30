using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BetterGenshinImpact.Core.Config;
using Microsoft.VisualBasic.FileIO;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>按标准名称加载公共目录；分类缺失不阻止数量上报。</summary>
public sealed class InventoryCatalog
{
    /// <summary>中文 CSV 展示文本共用的排序区域，不随操作系统区域变化。</summary>
    /// <remarks>TODO：接入项目 i18n 后跟随展示语言；切换语言时由调用方刷新集合视图的 Culture 和排序。</remarks>
    public CultureInfo TextSortCulture => CultureInfo.GetCultureInfo("zh-CN");

    /// <summary>供分类等非集合视图列表复用的文本比较器；不用于存储键的相等性判断。</summary>
    public StringComparer TextComparer => StringComparer.Create(TextSortCulture, false);

    /// <summary>解析物品展示名称；当前返回中文标准名称，不改变库存关联键。</summary>
    /// <remarks>TODO：供项目 i18n 接入物品翻译，缺失翻译时回退 standardName；届时展示、搜索与名称排序统一使用解析结果。</remarks>
    public string ResolveItemName(string standardName) => standardName;

    /// <summary>生成物品名称排序文本，忽略开头连续的常见双引号和左括号，保留括号内文字及其余标点。</summary>
    public string GetItemNameSortKey(string standardName)
    {
        var name = ResolveItemName(standardName);
        // 仅跳过前缀符号，不删除名称中间的标点，也不跳过括号内的限定文字。
        var start = 0;
        while (start < name.Length && name[start] is '「' or '『' or '"' or '＂' or '“' or '”'
               or '(' or '（' or '[' or '［' or '【' or '〔' or '{' or '｛' or '〈' or '《')
            start++;
        return name[start..];
    }

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
        var headers = parser.ReadFields() ?? throw new InvalidDataException("内置物品字典缺少表头，无法识别名称和分类列。");
        var nameIndex = Array.IndexOf(headers, "item_name");
        var categoryIndex = Array.IndexOf(headers, "material_type");
        if (nameIndex < 0 || categoryIndex < 0) throw new InvalidDataException("内置物品字典缺少名称或类型列，无法校验物品。");
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
        if (items.Count == 0) throw new InvalidDataException("内置物品字典中没有可用条目，暂不能校验库存名称。");
        return items;
    }
}
