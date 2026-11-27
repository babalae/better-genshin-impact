using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>公共物品字典的字段合并与冲突检查。</summary>
public sealed partial class InventoryStore
{
    /// <summary>对照当前 CSV 与补充值；预置字段的有意覆写不提示，备注不参与比较。</summary>
    public Task<Dictionary<string, string>> GetDictionaryConflictsAsync() => Locked(() =>
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(_root, "item-overrides.json");
        if (!File.Exists(path)) return result;
        var presets = CreatePresetCategories();
        foreach (var (name, entry) in Read<Dictionary<string, InventoryItemOverride>>(path, restoreBackup: false))
        {
            if (!_catalog.Items.TryGetValue(name, out var csvCategory)) continue;
            presets.TryGetValue(name, out var preset);
            var differences = new List<string>();
            // CSV 留空属于补缺；相同值和未修改的默认覆写均不构成待处理冲突。
            if (!string.IsNullOrEmpty(csvCategory) && entry.Category != csvCategory && entry.Category != preset?.Category)
                differences.Add($"Category：CSV = {csvCategory}；补充 = {entry.Category}");
            var csvPage = _catalog.GetPage(name);
            if (entry.Page != null && !string.IsNullOrEmpty(csvPage) && entry.Page != csvPage && entry.Page != preset?.Page)
                differences.Add($"Page：CSV = {csvPage}；补充 = {(entry.Page.Length == 0 ? "（未分类）" : entry.Page)}");
            if (differences.Count > 0) result[name] = string.Join(Environment.NewLine, differences);
        }
        return result;
    }, initialize: false);

    /// <summary>无账号时也能读取完整的分类与分页快照，用于首次生成筛选器。</summary>
    public Task<(Dictionary<string, string> Categories, Dictionary<string, string> Pages)> GetItemClassificationsAsync() =>
        Locked(() => (Definitions(restoreBackup: false), PageDefinitions(restoreBackup: false)), initialize: false);

    /// <summary>自动输入只补空字段；已有非空值或显式空覆写保持不变，并返回冲突说明。</summary>
    private static string? MergeDefinitionField(string? local, string? incoming, string field, List<string> conflicts, bool explicitEmpty = false)
    {
        if (incoming == null) return local;
        if (string.IsNullOrEmpty(local) && !explicitEmpty) return incoming;
        if (local != incoming) conflicts.Add($"{field}：本地“{local}”，传入“{incoming}”；已保留本地值，请修订字典或来源。");
        return local;
    }

    /// <summary>在已持有存储锁且上报有效时补齐分类，并将来源说明写入可读备注。</summary>
    private void SupplementReportedDefinition(string name, string category, IEnumerable<string?> comments, string source,
        DateTimeOffset now, Dictionary<string, string> definitions)
    {
        var path = Path.Combine(_root, "item-overrides.json");
        var entries = Read<Dictionary<string, InventoryItemOverride>>(path);
        if (!entries.TryGetValue(name, out var entry)) entry = new InventoryItemOverride();
        var notes = new[] { entry.Comment }.Concat(comments).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct();
        entry.Comment = string.Join(Environment.NewLine, notes.Append($"[{now:O}] 根据来源 {source} 的有效上报补充分类：{category}。"));
        entry.Category = MergeDefinitionField(definitions.GetValueOrDefault(name), category, "分类", [])!;
        entries[name] = entry;
        Write(path, entries);
        definitions[name] = entry.Category;
    }

    /// <summary>按物品叠加显式分页覆写，不根据分类名称推断分页。</summary>
    private Dictionary<string, string> PageDefinitions(bool restoreBackup = true)
    {
        var pages = _catalog.Items.Keys.ToDictionary(name => name, _catalog.GetPage, StringComparer.Ordinal);
        var path = Path.Combine(_root, "item-overrides.json");
        var entries = File.Exists(path) ? Read<Dictionary<string, InventoryItemOverride>>(path, restoreBackup) : CreatePresetCategories();
        foreach (var (name, entry) in entries)
            pages[name] = entry.Page ?? _catalog.GetPage(name);
        return pages;
    }


}
