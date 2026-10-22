using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls.CombatStrategy;

/// <summary>策略浏览器中的一行，承载后台读取的副标题及当前选中状态。</summary>
public partial class StrategyEntryViewModel : ObservableObject
{
    /// <summary>不可变目录节点。</summary>
    public CombatStrategyNode Node { get; }
    /// <summary>文件夹或文件图标文字。</summary>
    public string Icon => Node.IsFolder ? "📁" : IsCurrent ? "✓" : "";
    /// <summary>类型或子树计数。</summary>
    public string Badge => Node.IsFolder ? $"{Node.FileCount}  ›" : HasError ? "解析失败" : Node.Strategy?.Kind switch
    {
        CombatStrategyKind.Txt => "txt", CombatStrategyKind.Json => "JSON", _ => "键鼠",
    };
    /// <summary>角色、录制信息或搜索结果路径。</summary>
    [ObservableProperty] private string _subtitle = "";
    /// <summary>是否解析失败。</summary>
    [ObservableProperty] private bool _hasError;
    /// <summary>是否为当前选中策略。</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>创建列表行。</summary>
    public StrategyEntryViewModel(CombatStrategyNode node) => Node = node;
    /// <summary>刷新状态图标。</summary>
    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(Icon));
    /// <summary>刷新错误标签。</summary>
    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(Badge));
}
