using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls.CombatStrategy;

/// <summary>策略选择器的一级类别，不直接修改宿主配置。</summary>
public abstract partial class StrategyCategoryViewModel : ObservableObject
{
    /// <summary>类别对应的单一策略类型。</summary>
    public CombatStrategyKind Kind { get; }
    /// <summary>类别标题。</summary>
    public string Title { get; }
    /// <summary>是否属于自动选择。</summary>
    public bool IsAuto => Kind is CombatStrategyKind.AutoSelect or CombatStrategyKind.Combo;
    /// <summary>一级分组标题。</summary>
    public string GroupName => IsAuto ? "自动选择" : "手动指定";
    /// <summary>类别内策略总数。</summary>
    [ObservableProperty] private int _count;
    /// <summary>搜索命中数；没有搜索时为 null。</summary>
    [ObservableProperty] private int? _matchCount;
    /// <summary>类别右侧显示的数量。</summary>
    public string CountText => IsAuto ? "" : (MatchCount ?? Count).ToString();

    /// <summary>初始化类别标识与标题。</summary>
    protected StrategyCategoryViewModel(CombatStrategyKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    /// <summary>数量变化时刷新一级菜单。</summary>
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountText));
    /// <summary>搜索变化时刷新一级菜单。</summary>
    partial void OnMatchCountChanged(int? value) => OnPropertyChanged(nameof(CountText));

    /// <summary>关闭或卸载时取消后台读取。</summary>
    public virtual void CancelLoading() { }
}
