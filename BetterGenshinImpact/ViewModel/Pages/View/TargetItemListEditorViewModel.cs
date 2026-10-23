using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Reward;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>
/// 独立任务页的“材料目标”列表编辑器：直接读写配置中的目标集合，空集合等同于关闭。
/// </summary>
public partial class TargetItemListEditorViewModel : ObservableObject
{
    /// <summary>
    /// 当前任务配置中的材料目标集合。
    /// </summary>
    public ObservableCollection<TargetItem> Items { get; }

    /// <summary>
    /// 当前首领、秘境或地脉花类型可选的奖励物品。
    /// </summary>
    public ObservableCollection<TargetItemOption> Choices { get; } = [];

    /// <summary>
    /// 当前已订阅属性变化的材料目标行。
    /// </summary>
    private readonly HashSet<TargetItem> _trackedItems = [];

    /// <summary>
    /// 每个材料目标最近一次有效的物品名，用于拦截重复选择。
    /// </summary>
    private readonly Dictionary<TargetItem, string> _lastValidNames = [];

    /// <summary>
    /// 正在回退重复选择，避免属性变化再次进入。
    /// </summary>
    private bool _revertingDuplicate;

    /// <summary>
    /// 是否启用材料目标；关闭时清空目标集合，开启时至少保留一行。
    /// </summary>
    [ObservableProperty]
    private bool _isTargetModeEnabled;

    /// <summary>
    /// 是否刚刚尝试选择了重复材料，供界面显示提示。
    /// </summary>
    [ObservableProperty]
    private bool _hasDuplicateAttempt;

    /// <summary>
    /// 是否存在可选的奖励物品。
    /// </summary>
    public bool HasChoices => Choices.Count > 0;

    /// <summary>
    /// 是否没有可选的奖励物品，供页面显示不可用提示。
    /// </summary>
    public bool IsUnavailable => !HasChoices;

    /// <summary>
    /// 绑定配置中的目标集合；集合非空时视为已开启。
    /// </summary>
    /// <param name="items">任务配置里的材料目标集合。</param>
    public TargetItemListEditorViewModel(ObservableCollection<TargetItem> items)
    {
        Items = items;
        Items.CollectionChanged += OnItemsCollectionChanged;
        SyncTrackedItems();
        RemoveDuplicateItems();
        _isTargetModeEnabled = items.Count > 0;
    }

    /// <summary>
    /// 按当前首领、秘境或地脉花类型更新候选物品，并移除已不属于候选的目标。
    /// </summary>
    /// <param name="choices">新的候选物品。</param>
    public void ApplyChoices(IReadOnlyList<TargetItemOption> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        HasDuplicateAttempt = false;
        var allowed = choices.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        // 先移除不在候选中的目标，避免替换下拉选项时把无效目标回写成空名称
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!allowed.Contains(Items[i].ItemName))
            {
                Items.RemoveAt(i);
            }
        }

        // 差量更新选项并保留仍有效的选项实例，避免 ComboBox 在重置选项时清空已选值
        for (var i = Choices.Count - 1; i >= 0; i--)
        {
            if (!allowed.Contains(Choices[i].Name))
            {
                Choices.RemoveAt(i);
            }
        }

        var existing = Choices.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            if (existing.Add(choice.Name))
            {
                Choices.Add(choice);
            }
        }

        OnPropertyChanged(nameof(HasChoices));
        OnPropertyChanged(nameof(IsUnavailable));
        AddCommand.NotifyCanExecuteChanged();

        // 目标被候选裁剪干净时同步关闭开关，保持“空集合即关闭”的语义
        if (Items.Count == 0 && IsTargetModeEnabled)
        {
            IsTargetModeEnabled = false;
        }
    }

    /// <summary>
    /// 打开或关闭材料目标；关闭时清空目标集合。
    /// </summary>
    /// <param name="value">是否启用材料目标。</param>
    partial void OnIsTargetModeEnabledChanged(bool value)
    {
        if (!value)
        {
            if (Items.Count > 0)
            {
                Items.Clear();
            }

            HasDuplicateAttempt = false;
            AddCommand.NotifyCanExecuteChanged();
            return;
        }

        if (Items.Count == 0)
        {
            AddFirstAvailable();
        }
    }

    /// <summary>
    /// 添加一个尚未使用的材料目标，数量默认为 1。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        AddFirstAvailable();
    }

    /// <summary>
    /// 删除指定的材料目标；删除最后一行后关闭材料目标。
    /// </summary>
    /// <param name="item">要删除的目标。</param>
    [RelayCommand]
    private void Remove(TargetItem? item)
    {
        if (item is null)
        {
            return;
        }

        Items.Remove(item);
        HasDuplicateAttempt = false;
        AddCommand.NotifyCanExecuteChanged();
        if (Items.Count == 0)
        {
            IsTargetModeEnabled = false;
        }
    }

    /// <summary>
    /// 是否还有未被使用的候选物品。
    /// </summary>
    private bool CanAdd()
    {
        return Choices.Any(choice => Items.All(item =>
            !string.Equals(item.ItemName, choice.Name, StringComparison.Ordinal)));
    }

    /// <summary>
    /// 添加第一个未使用的候选物品。
    /// </summary>
    private void AddFirstAvailable()
    {
        var choice = Choices.FirstOrDefault(candidate => Items.All(item =>
            !string.Equals(item.ItemName, candidate.Name, StringComparison.Ordinal)));
        if (choice is null)
        {
            return;
        }

        Items.Add(new TargetItem(choice.Name, 1));
        HasDuplicateAttempt = false;
        AddCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 目标集合变化后同步行属性订阅，覆盖新增、删除与 Items.Clear() 的重置。
    /// </summary>
    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncTrackedItems();
    }

    /// <summary>
    /// 为当前目标行订阅物品名变化，并清理已移除行的订阅和快照。
    /// </summary>
    private void SyncTrackedItems()
    {
        foreach (var item in _trackedItems.Except(Items).ToList())
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            _trackedItems.Remove(item);
            _lastValidNames.Remove(item);
        }

        foreach (var item in Items)
        {
            if (!_trackedItems.Add(item))
            {
                continue;
            }

            _lastValidNames[item] = item.ItemName;
            item.PropertyChanged += OnItemPropertyChanged;
        }
    }

    /// <summary>
    /// 保留每个物品名的第一行，移除历史配置中的重复项。
    /// </summary>
    private void RemoveDuplicateItems()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = Items.Where(item => !names.Add(item.ItemName)).ToList();
        foreach (var item in duplicates)
        {
            Items.Remove(item);
        }
    }

    /// <summary>
    /// 拦截重复或空物品名：回退到上一次有效名称，并提示用户。
    /// </summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_revertingDuplicate || e.PropertyName != nameof(TargetItem.ItemName) || sender is not TargetItem item
            || !_trackedItems.Contains(item))
        {
            return;
        }

        var duplicate = string.IsNullOrEmpty(item.ItemName) || Items.Any(other =>
            !ReferenceEquals(other, item) && string.Equals(other.ItemName, item.ItemName, StringComparison.Ordinal));
        if (duplicate)
        {
            if (_lastValidNames.TryGetValue(item, out var previousName))
            {
                _revertingDuplicate = true;
                try
                {
                    item.ItemName = previousName;
                }
                finally
                {
                    _revertingDuplicate = false;
                }
            }

            HasDuplicateAttempt = true;
        }
        else
        {
            _lastValidNames[item] = item.ItemName;
            HasDuplicateAttempt = false;
        }

        // 行内物品名变化会影响可选候选，刷新“添加材料”的可用状态
        AddCommand.NotifyCanExecuteChanged();
    }
}
