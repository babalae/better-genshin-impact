using BetterGenshinImpact.GameTask.Common.Resin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>固定使用顺序中的一行树脂；隐藏的开关和次数分别保留。</summary>
public partial class ResinKindRowViewModel : ObservableObject
{
    /// <summary>本行对应的树脂。</summary>
    public ResinKind Kind { get; }
    /// <summary>树脂全称。</summary>
    public string DisplayName => ResinUsageText.DisplayName(Kind);
    /// <summary>本次是否允许使用。</summary>
    [ObservableProperty] private bool _isAllowed;
    /// <summary>按树脂分别指定的领奖次数。</summary>
    [ObservableProperty] private int _count;
    /// <summary>行说明或不可用原因。</summary>
    [ObservableProperty] private string _description = string.Empty;
    /// <summary>当前控件是否允许编辑。</summary>
    [ObservableProperty] private bool _isEditable;
    /// <summary>是否显示开关。</summary>
    [ObservableProperty] private bool _showToggle;
    /// <summary>是否显示次数框。</summary>
    [ObservableProperty] private bool _showCount;
    /// <summary>读屏名称，随开关或次数框切换。</summary>
    [ObservableProperty] private string _automationName = string.Empty;

    /// <summary>从当前设置建立一行，直接初始化字段，不发送编辑事件。</summary>
    public ResinKindRowViewModel(ResinKind kind, bool isAllowed, int count)
    {
        Kind = kind;
        _isAllowed = isAllowed;
        _count = count;
    }

    /// <summary>只更新显示状态，不修改开关与次数，避免切换方式时丢值。</summary>
    internal void UpdatePresentation(ResinUsageMode mode, ResinUsageCapability capability)
    {
        ShowCount = mode == ResinUsageMode.PerType;
        ShowToggle = !ShowCount;
        IsEditable = capability.IsSupported(Kind) && (ShowCount || capability.LockedKind != Kind);
        Description = ResinUsageText.KindDescription(Kind, mode, capability);
        AutomationName = DisplayName + (ShowCount ? "领奖次数" : "使用开关");
    }
}
