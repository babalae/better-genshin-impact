using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.VisualBasic.FileIO;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>字典只读预览项，使用独立标识区分同名记录。</summary>
public sealed record AccountRecordsDictionaryEntry(string Name, string Details, string Key = "");

/// <summary>公共目录只读；补充字典和来源字典支持外部编辑，补充字典可恢复预置分类。</summary>
public partial class AccountRecordsDictionaryViewModel : ObservableObject
{
    /// <summary>当前预览文件路径。</summary>
    private readonly string _path;
    /// <summary>共用物品目录的展示排序规则。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>提供只读的 CSV 冲突对照，不在预览时修改补充字典。</summary>
    private readonly InventoryStore? _store;
    /// <summary>由存储层提供恢复操作，视图模型不直接改写字典。</summary>
    private readonly Func<Task>? _restorePresetCategories;
    /// <summary>完整预览快照，包含同名但代码不同的 CSV 行。</summary>
    private AccountRecordsDictionaryEntry[] _entries = [];
    /// <summary>窗口名称。</summary>
    public string Title { get; }
    /// <summary>公共目录不允许打开源文件编辑。</summary>
    public bool CanEdit { get; }
    /// <summary>仅补充字典显示初始值维护提醒。</summary>
    public bool IsSupplementaryDictionary { get; }
    /// <summary>显式开启外部编辑入口。</summary>
    [ObservableProperty] private bool _editingEnabled;
    /// <summary>名称或原始字段搜索。</summary>
    [ObservableProperty] private string _searchText = "";
    /// <summary>当前选中项。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Details))] private AccountRecordsDictionaryEntry? _selected;
    /// <summary>读取状态。</summary>
    [ObservableProperty] private string _message = "";
    /// <summary>过滤后的项目。</summary>
    public ObservableCollection<AccountRecordsDictionaryEntry> Entries { get; } = [];
    /// <summary>供复制的只读内容。</summary>
    public string Details => Selected?.Details ?? "请选择条目。";

    /// <summary>固定文件与权限，避免通过界面修改公共目录。</summary>
    public AccountRecordsDictionaryViewModel(string title, string path, bool canEdit, InventoryCatalog catalog,
        bool isSupplementaryDictionary = false, Func<Task>? restorePresetCategories = null, InventoryStore? store = null)
    {
        Title = title; _path = path; CanEdit = canEdit; _catalog = catalog; _store = store;
        IsSupplementaryDictionary = isSupplementaryDictionary;
        _restorePresetCategories = restorePresetCategories;
    }

    /// <summary>确认后恢复预置分类，补回缺失项并保留用户新增条目。</summary>
    [RelayCommand]
    private async Task RestorePresetCategories()
    {
        if (!CanEdit || !IsSupplementaryDictionary || !EditingEnabled || _restorePresetCategories == null) return;
        if (await ThemedMessageBox.ShowAsync("将补回缺失的预置物品，并将它们的分类恢复为默认值。用户新增的其他条目会保留，库存数量不变。\n此字典由所有 UID 共用；原文件将保存为 .bak 备份。是否继续？",
                "恢复预置分类", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _restorePresetCategories());
            await Refresh();
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(AccountRecordsErrorMessages.Describe(ex), "恢复预置分类", MessageBoxButton.OK); }
    }

    /// <summary>查询变化时更新列表。</summary>
    partial void OnSearchTextChanged(string value) => Filter();

    /// <summary>重新载入文件；公共目录保留全部记录及除特征向量外的原始字段。</summary>
    [RelayCommand]
    public async Task Refresh()
    {
        try
        {
            var entries = await Task.Run(() => !CanEdit
                ? ReadItemEntries()
                : File.Exists(_path)
                    ? ReadDictionary().Properties().Select(property =>
                        new AccountRecordsDictionaryEntry(property.Name, property.Value.ToString(Formatting.Indented))).ToArray()
                    : []);
            var conflicts = IsSupplementaryDictionary && _store != null
                ? await _store.GetDictionaryConflictsAsync() : new Dictionary<string, string>();
            _entries = entries.Select(item => conflicts.TryGetValue(item.Name, out var conflict)
                ? item with { Details = item.Details + Environment.NewLine + Environment.NewLine + "CSV 冲突（当前仍采用补充值）：" + Environment.NewLine + conflict }
                : item).OrderBy(item => item.Name, _catalog.TextComparer).ToArray();
            Filter();
            Message = CanEdit && !File.Exists(_path) ? "字典文件尚未生成。" : $"共 {_entries.Length} 项。";
            if (conflicts.Count > 0) Message += $" {conflicts.Count} 项与 CSV 冲突，搜索“CSV 冲突”查看；默认预置覆写已忽略。";
        }
        catch (Exception ex)
        {
            _entries = [];
            Filter();
            Message = $"读取失败：{AccountRecordsErrorMessages.Describe(ex)}";
        }
    }

    /// <summary>按 CSV 表头顺序展示非向量字段；空字段保留代码标签，同名记录不合并。</summary>
    private AccountRecordsDictionaryEntry[] ReadItemEntries()
    {
        using var parser = new TextFieldParser(_path, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("物品字典缺少表头。");
        var nameIndex = Array.IndexOf(headers, "item_name");
        var variantIndex = Array.IndexOf(headers, "variant_id");
        if (nameIndex < 0) throw new InvalidDataException("物品字典缺少 item_name 列。");
        var entries = new List<AccountRecordsDictionaryEntry>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields == null) continue;
            // 保留原值和空值；表头新增字段时直接以代码名展示，无须修改查看器。
            var details = new StringBuilder();
            for (var index = 0; index < headers.Length; index++)
            {
                if (headers[index] == "embedding") continue;
                if (details.Length > 0) details.AppendLine();
                details.Append(headers[index]).Append(": ");
                if (index < fields.Length) details.Append(fields[index]);
            }
            var name = nameIndex < fields.Length ? fields[nameIndex] : "";
            var variant = variantIndex >= 0 && variantIndex < fields.Length ? fields[variantIndex] : "";
            var key = variant.Length > 0 ? variant : $"row:{entries.Count}";
            entries.Add(new AccountRecordsDictionaryEntry(name, details.ToString(), key));
        }
        return entries.ToArray();
    }

    /// <summary>与库存读取使用相同校验，避免查看器接受存储层拒绝的字典。</summary>
    private JObject ReadDictionary()
    {
        var text = InventoryStore.NormalizeUserDictionary(_path, File.ReadAllText(_path));
        InventoryStore.ValidateDictionaryFile(_path, text);
        return JObject.Parse(text);
    }

    /// <summary>保留选中条目并过滤，不修改实际字典。</summary>
    private void Filter()
    {
        var selected = Selected;
        var query = SearchText.Trim();
        Entries.Clear();
        foreach (var item in _entries.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Details.Contains(query, StringComparison.OrdinalIgnoreCase))) Entries.Add(item);
        Selected = Entries.FirstOrDefault(item => selected != null && item.Key == selected.Key && item.Name == selected.Name) ?? Entries.FirstOrDefault();
    }

    /// <summary>只允许补充字典与来源 JSON 交给默认编辑程序。</summary>
    [RelayCommand]
    private async Task OpenSource()
    {
        if (!CanEdit || !EditingEnabled) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() =>
            {
                if (!File.Exists(_path)) throw new FileNotFoundException("字典文件尚未生成。", _path);
                using var process = Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true });
                return Task.CompletedTask;
            });
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(AccountRecordsErrorMessages.Describe(ex), "打开源文件", MessageBoxButton.OK); }
    }
}
