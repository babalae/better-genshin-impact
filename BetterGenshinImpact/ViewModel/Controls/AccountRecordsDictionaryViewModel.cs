using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>字典只读预览项，保留完整原始字段供检查。</summary>
public sealed record AccountRecordsDictionaryEntry(string Name, string Details);

/// <summary>公共目录只读；补充字典和来源字典支持外部编辑，补充字典可恢复预置分类。</summary>
public partial class AccountRecordsDictionaryViewModel : ObservableObject
{
    private readonly string _path;
    private readonly InventoryCatalog _catalog;
    /// <summary>由存储层提供恢复操作，视图模型不直接改写字典。</summary>
    private readonly Func<Task>? _restorePresetCategories;
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
        bool isSupplementaryDictionary = false, Func<Task>? restorePresetCategories = null)
    {
        Title = title; _path = path; CanEdit = canEdit; _catalog = catalog;
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

    /// <summary>重新载入外部编辑后的文件；公共目录仅读取物品名称与分类。</summary>
    [RelayCommand]
    public async Task Refresh()
    {
        try
        {
            var entries = await Task.Run(() => !CanEdit
                ? _catalog.Items.Select(pair => new AccountRecordsDictionaryEntry(pair.Key, $"名称：{pair.Key}\n分类：{pair.Value}")).ToArray()
                : File.Exists(_path)
                    ? ReadDictionary().Properties().Select(property =>
                        new AccountRecordsDictionaryEntry(property.Name, property.Value.ToString(Formatting.Indented))).ToArray()
                    : []);
            _entries = entries.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToArray();
            Filter();
            Message = CanEdit && !File.Exists(_path) ? "字典文件尚未生成。" : $"共 {_entries.Length} 项。";
        }
        catch (Exception ex)
        {
            _entries = [];
            Filter();
            Message = $"读取失败：{AccountRecordsErrorMessages.Describe(ex)}";
        }
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
        var name = Selected?.Name;
        var query = SearchText.Trim();
        Entries.Clear();
        foreach (var item in _entries.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Details.Contains(query, StringComparison.OrdinalIgnoreCase))) Entries.Add(item);
        Selected = Entries.FirstOrDefault(item => item.Name == name) ?? Entries.FirstOrDefault();
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
