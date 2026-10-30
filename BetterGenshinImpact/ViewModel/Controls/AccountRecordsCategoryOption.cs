using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>一个可勾选的物品分类。</summary>
public partial class AccountRecordsCategoryOption : ObservableObject
{
    /// <summary>分类显示名称。</summary>
    public string Name { get; }
    /// <summary>通知所属表格重新筛选。</summary>
    private readonly Action _changed;
    /// <summary>默认包含该分类。</summary>
    [ObservableProperty] private bool _isSelected = true;
    /// <summary>创建分类项。</summary>
    public AccountRecordsCategoryOption(string name, Action changed) { Name = name; _changed = changed; }
    /// <summary>勾选改变后立即更新结果。</summary>
    partial void OnIsSelectedChanged(bool value) => _changed();
}
