using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>一个可勾选的物品分类。</summary>
public partial class AccountRecordsCategoryOption : ObservableObject
{
    /// <summary>分类显示名称。</summary>
    public string Name { get; set; }
    /// <summary>通知所属表格重新筛选。</summary>
    private readonly Action _changed;
    /// <summary>叶节点的选中状态。</summary>
    private bool _isSelected = true;
    /// <summary>父节点批量更新期间合并通知。</summary>
    private bool _updatingChildren;
    /// <summary>所属背包页；空字符串表示未分类。</summary>
    public string Page { get; }
    /// <summary>本次页面会话的展开状态，不影响筛选勾选。</summary>
    [ObservableProperty]
    private bool _isExpanded;
    /// <summary>背包页分组标题使用的共享图标，缺失时保留空白位置。</summary>
    public ImageSource? PageIcon => AccountRecordsPageIcons.Get(Page);
    /// <summary>实际材料类型，区别于未细分的展示名称。</summary>
    public string Category { get; }
    /// <summary>背包页下的材料类型，叶节点为空。</summary>
    public IReadOnlyList<AccountRecordsCategoryOption> Children { get; } = [];
    /// <summary>父节点使用半选态表达部分类型被选中。</summary>
    public bool? IsSelected
    {
        get => Children.Count == 0 ? _isSelected
            : Children.All(child => child.IsSelected == true) ? true
            : Children.All(child => child.IsSelected == false) ? false : null;
        set
        {
            if (value == null) return;
            if (Children.Count == 0)
            {
                if (SetProperty(ref _isSelected, value.Value)) _changed();
                return;
            }
            _updatingChildren = true;
            try { foreach (var child in Children) child.IsSelected = value; }
            finally { _updatingChildren = false; }
            ChildChanged();
        }
    }
    /// <summary>创建分类项。</summary>
    public AccountRecordsCategoryOption(string name, Action changed, string page = "", string category = "")
    {
        Name = name; _changed = changed; Page = page; Category = category;
        _isExpanded = page is "养成道具" or "材料" or "贵重道具";
    }

    /// <summary>创建背包页及其独立筛选的材料类型。</summary>
    public AccountRecordsCategoryOption(string page, IEnumerable<string> categories, Action changed)
        : this(string.IsNullOrEmpty(page) ? "未分类" : page, changed, page)
    {
        Children = categories.Select(category => new AccountRecordsCategoryOption(
            string.IsNullOrEmpty(category) ? "未细分" : category, ChildChanged, page, category)).ToArray();
    }

    /// <summary>同步父节点勾选状态并刷新筛选。</summary>
    private void ChildChanged()
    {
        if (_updatingChildren) return;
        OnPropertyChanged(nameof(IsSelected));
        _changed();
    }
}
