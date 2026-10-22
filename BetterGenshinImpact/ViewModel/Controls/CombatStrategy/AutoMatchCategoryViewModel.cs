using System;
using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls.CombatStrategy;

/// <summary>按队伍匹配类别，范围只包含文本策略。</summary>
public partial class AutoMatchCategoryViewModel : StrategyCategoryViewModel
{
    /// <summary>统一选择回调。</summary>
    private readonly Action<CombatStrategyRef> _select;
    /// <summary>查看错误列表的回调。</summary>
    private readonly Action _showErrors;
    /// <summary>全部范围及各级含文本策略的目录。</summary>
    public ObservableCollection<AutoMatchScopeViewModel> Scopes { get; } = [];
    /// <summary>当前可见范围行，供键盘选择使用。</summary>
    [ObservableProperty] private AutoMatchScopeViewModel? _selectedScope;
    /// <summary>解析失败数量；最多统计 200 个文件的提示由 MetadataStatus 显示。</summary>
    [ObservableProperty] private string _errorSummary = "";

    /// <summary>检查进度、统计范围限制或加载失败提示。</summary>
    [ObservableProperty] private string _metadataStatus = "";
    /// <summary>只在存在解析失败项时显示查看入口。</summary>
    public bool HasErrors => ErrorSummary.Length > 0;
    /// <summary>更新解析失败入口的可见性。</summary>
    partial void OnErrorSummaryChanged(string value) => OnPropertyChanged(nameof(HasErrors));

    /// <summary>创建自动匹配类别。</summary>
    public AutoMatchCategoryViewModel(Action<CombatStrategyRef> select, Action showErrors)
        : base(CombatStrategyKind.AutoSelect, "按队伍匹配")
    {
        _select = select;
        _showErrors = showErrors;
    }

    /// <summary>选择匹配范围。</summary>
    [RelayCommand]
    private void Select(AutoMatchScopeViewModel? scope)
    {
        if (scope != null) _select(CombatStrategyResolver.Parse(scope.Value));
    }

    /// <summary>跳转文本策略的解析失败过滤结果。</summary>
    [RelayCommand]
    private void ShowErrors() => _showErrors();
}

/// <summary>一行匹配范围，保留深度信息以显示树形缩进。</summary>
/// <param name="Node">目录树中的范围节点。</param>
/// <param name="IsCurrent">是否为当前匹配范围。</param>
public sealed record AutoMatchScopeViewModel(CombatStrategyNode Node, bool IsCurrent)
{
    /// <summary>要持久化的范围引用值。</summary>
    public string Value => Node.RelativePath.Length == 0 ? CombatStrategyResolver.AutoSelectName : CombatStrategyResolver.AutoScopePrefix + Node.RelativePath;
    /// <summary>范围显示名称，根目录使用业务名称。</summary>
    public string Name => Node.RelativePath.Length == 0 ? "全部文本策略" : Node.Name;
    /// <summary>带数量和当前标记的树形显示文字，供无障碍提示使用。</summary>
    public string Title => $"{(IsCurrent ? "已选：" : "")}{Name} · {Node.FileCount}";
    /// <summary>嵌套目录的缩进层级；全部范围与一级目录左对齐。</summary>
    public int Depth => Node.RelativePath.Count(c => c == '\\' || c == '/');
}
