using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 共享设置编辑器左侧导航中的一个类别。
/// </summary>
public partial class PuloniaSharedSettingCategoryViewModel : ObservableObject
{
    /// <summary>类别定义。</summary>
    public PuloniaSettingCategory Category { get; }

    /// <summary>类别名称。</summary>
    public string DisplayName => Category.DisplayName;

    /// <summary>类别中的全部行（不论是否可见）。</summary>
    public IReadOnlyList<PuloniaSharedSettingRowViewModel> Rows { get; }

    /// <summary>编辑对象在本类别中覆盖的项数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverrides))]
    private int _overrideCount;

    /// <summary>本类别有覆盖项。</summary>
    public bool HasOverrides => OverrideCount > 0;

    /// <summary>影响任务数说明；为空时不显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUsageText))]
    private string? _usageText;

    /// <summary>是否显示影响任务数说明。</summary>
    public bool HasUsageText => !string.IsNullOrEmpty(UsageText);

    /// <summary>编辑对象下没有任务使用本类别；导航中置灰，但仍可设置。</summary>
    [ObservableProperty] private bool _isUnused;

    /// <summary>搜索命中数；不在搜索时为 0。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches))]
    private int _matchCount;

    /// <summary>是否显示搜索命中数。</summary>
    public bool HasMatches => MatchCount > 0;

    /// <summary>建立类别。</summary>
    internal PuloniaSharedSettingCategoryViewModel(PuloniaSettingCategory category, IReadOnlyList<PuloniaSharedSettingRowViewModel> rows)
    {
        Category = category;
        Rows = rows;
    }
}
