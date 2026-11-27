using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.AccountRecords;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>按当前表的背包页和类型选择要清理的物品，不改变表格筛选状态。</summary>
public partial class AccountRecordsIconCleanupViewModel : ObservableObject
{
    /// <summary>打开窗口时的物品及分类快照。</summary>
    private readonly (string Name, string Page, string Category)[] _items;
    /// <summary>仅包含当前表实际存在的分类，结构与筛选器一致。</summary>
    public IReadOnlyList<AccountRecordsCategoryOption> Categories { get; }
    /// <summary>确认后由调用方执行清理。</summary>
    public bool Accepted { get; private set; }
    /// <summary>请求视图关闭窗口。</summary>
    public event Action? CloseRequested;
    /// <summary>被选中分类对应的物品名称。</summary>
    public string[] SelectedNames => _items.Where(item => Categories.SelectMany(group => group.Children)
        .Any(child => child.IsSelected == true && child.Page == item.Page && child.Category == item.Category))
        .Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
    /// <summary>无选择时禁止确认。</summary>
    public bool HasSelection => SelectedNames.Length > 0;
    /// <summary>创建独立选项；默认不勾选，避免误清理。</summary>
    public AccountRecordsIconCleanupViewModel(IEnumerable<AccountRecordsInventoryRow> rows)
    {
        _items = rows.Select(row => (row.Name, row.Page, row.MaterialType)).Distinct().ToArray();
        Categories = _items.GroupBy(item => item.Page).OrderBy(group => InventoryCatalog.GetPageSortOrder(group.Key))
            .Select(group => new AccountRecordsCategoryOption(group.Key,
                group.Select(item => item.Category).Distinct().OrderBy(name => name), SelectionChanged)).ToArray();
        foreach (var category in Categories) category.IsSelected = false;
    }
    /// <summary>更新确认按钮可用性。</summary>
    private void SelectionChanged() { OnPropertyChanged(nameof(HasSelection)); ConfirmCommand.NotifyCanExecuteChanged(); }
    /// <summary>全选当前表实际存在的分类。</summary>
    [RelayCommand] private void SelectAll() { foreach (var category in Categories) category.IsSelected = true; }
    /// <summary>确认当前选择。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))] private void Confirm() { Accepted = true; CloseRequested?.Invoke(); }
    /// <summary>取消不改动缓存。</summary>
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke();
}
