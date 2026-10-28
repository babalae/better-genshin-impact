using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>任务树行尾的路线 CD 标记：由编辑会话按账本与当前规则计算，不持久化、不参与执行判断。</summary>
public partial class PuloniaTaskNodeViewModel
{
    /// <summary>标记文字，例如“CD 中 · 剩 5 小时”“5/20 CD 中”“未识别 CD”；为空时不显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCooldownBadge))]
    private string? _cooldownBadgeText;

    /// <summary>标记是否使用提示色（未识别刷新规则）；其余标记使用中性色。</summary>
    [ObservableProperty]
    private bool _isCooldownBadgeCaution;

    /// <summary>悬停说明：规则、上次成功与下次可执行时间，或分组的汇总。</summary>
    [ObservableProperty]
    private string? _cooldownToolTip;

    /// <summary>是否显示路线 CD 标记。</summary>
    public bool HasCooldownBadge => !string.IsNullOrEmpty(CooldownBadgeText);

    /// <summary>更新路线 CD 标记；内容不变时不触发通知。</summary>
    /// <param name="text">标记文字；为空时隐藏。</param>
    /// <param name="caution">是否使用提示色。</param>
    /// <param name="toolTip">悬停说明。</param>
    internal void SetCooldownBadge(string? text, bool caution, string? toolTip)
    {
        CooldownBadgeText = text;
        IsCooldownBadgeCaution = caution;
        CooldownToolTip = toolTip;
    }
}
