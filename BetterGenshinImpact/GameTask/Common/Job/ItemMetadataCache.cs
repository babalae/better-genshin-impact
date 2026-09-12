using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// item.csv 元数据缓存：按物品名反查背包页面与页内排序。
/// 加载失败时返回空缓存，不影响既有任务。
/// </summary>
public static class ItemMetadataCache
{
    private record struct ItemEntry(GridScreenName Page, int? SortOrder);
    private static readonly ILogger _logger = App.GetLogger<LoggerMarker>();
    private static readonly Dictionary<string, ItemEntry> _entries = LoadEntriesOrEmpty();

    /// <summary>
    /// 仅用于日志分类的占位类型
    /// </summary>
    private sealed class LoggerMarker { }

    /// <summary>
    /// 查物品所属背包页面。
    /// </summary>
    /// <param name="itemName">物品名称。</param>
    /// <param name="page">命中的背包页面。</param>
    /// <returns>命中返回 true；否则 false。</returns>
    public static bool TryGetPage(string itemName, out GridScreenName page)
    {
        if (_entries.TryGetValue(itemName, out var entry))
        {
            page = entry.Page;
            return true;
        }
        page = default;
        return false;
    }

    /// <summary>
    /// 查物品页内排序；空/无效返回 false。
    /// </summary>
    /// <param name="itemName">物品名称。</param>
    /// <param name="sortOrder">命中的页内排序。</param>
    /// <returns>命中且有效返回 true；否则 false。</returns>
    public static bool TryGetSortOrder(string itemName, out int sortOrder)
    {
        if (_entries.TryGetValue(itemName, out var entry) && entry.SortOrder.HasValue)
        {
            sortOrder = entry.SortOrder.Value;
            return true;
        }
        sortOrder = 0;
        return false;
    }

    /// <summary>
    /// 容错加载：异常时记日志并返回空字典，避免影响进程启动。
    /// </summary>
    /// <returns>物品元数据字典。</returns>
    private static Dictionary<string, ItemEntry> LoadEntriesOrEmpty()
    {
        try
        {
            return LoadEntries();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "加载物品元数据失败，将无法按物品名解析背包页面与排序");
            return new Dictionary<string, ItemEntry>();
        }
    }

    /// <summary>
    /// 读取 Assets/Model/ItemV2/item.csv，按 item_name 缓存 page 与 sort_order。
    /// </summary>
    /// <returns>物品元数据字典。</returns>
    private static Dictionary<string, ItemEntry> LoadEntries()
    {
        var dict = new Dictionary<string, ItemEntry>();
        using var parser = new TextFieldParser(Global.Absolute(@"Assets\Model\ItemV2\item.csv"), Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()!;
        int nameIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "item_name", StringComparison.OrdinalIgnoreCase));
        int pageIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "page", StringComparison.OrdinalIgnoreCase));
        int sortOrderIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "sort_order", StringComparison.OrdinalIgnoreCase));

        while (!parser.EndOfData)
        {
            var columns = parser.ReadFields()!;
            string name = columns[nameIndex].Trim();
            string pageStr = columns[pageIndex].Trim();
            string sortStr = columns[sortOrderIndex].Trim();

            if (!pageStr.TryGetEnumValueFromDescription(out GridScreenName? pageNullable) || pageNullable == null)
            {
                continue;
            }

            GridScreenName page = pageNullable.Value;
            int? sortOrder = null;
            if (!string.IsNullOrEmpty(sortStr) && int.TryParse(sortStr, out int parsed))
            {
                sortOrder = parsed;
            }

            dict[name] = new ItemEntry(page, sortOrder);
        }
        return dict;
    }
}
