using System;
using System.Linq;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 拖动移动后的共享设置提示：子树换了上层后若有任务的有效设置改变，显示非模态提示，
/// 用户可查看明细、保留原值（写到被移动的子树根上）或忽略（接受新位置的继承）。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>明细最多显示的行数，避免大子树移动时提示过长。</summary>
    private const int MaxMoveSettingsDetailLines = 30;

    /// <summary>最近一次移动造成的设置变化；为空表示没有待处理的提示。</summary>
    private PuloniaMovedSettingsChange? _pendingMoveSettingsChange;

    /// <summary>是否显示移动后的设置变化提示。</summary>
    [ObservableProperty]
    private bool _hasMoveSettingsNotice;

    /// <summary>提示正文，例如“移动后 3 个任务的 2 项设置已改变”。</summary>
    [ObservableProperty]
    private string _moveSettingsNoticeText = string.Empty;

    /// <summary>变化明细，每行一个任务的一项设置。</summary>
    [ObservableProperty]
    private string _moveSettingsNoticeDetails = string.Empty;

    /// <summary>明细是否展开。</summary>
    [ObservableProperty]
    private bool _isMoveSettingsDetailsVisible;

    /// <summary>
    /// 显示移动造成的设置变化；为空时关闭提示。
    /// </summary>
    private void ShowMoveSettingsNotice(PuloniaMovedSettingsChange? change)
    {
        _pendingMoveSettingsChange = change;
        IsMoveSettingsDetailsVisible = false;
        if (change is null)
        {
            HasMoveSettingsNotice = false;
            MoveSettingsNoticeText = string.Empty;
            MoveSettingsNoticeDetails = string.Empty;
            return;
        }

        MoveSettingsNoticeText = $"移动“{change.Node.Name}”后，{change.ChangedTaskCount} 个任务的 {change.ChangedKeys.Count} 项设置已改变。";
        var lines = change.Details.Take(MaxMoveSettingsDetailLines).ToList();
        if (change.Details.Count > MaxMoveSettingsDetailLines)
            lines.Add($"… 另有 {change.Details.Count - MaxMoveSettingsDetailLines} 项变化");
        MoveSettingsNoticeDetails = string.Join(Environment.NewLine, lines);
        HasMoveSettingsNotice = true;
    }

    /// <summary>展开或收起变化明细。</summary>
    [RelayCommand]
    private void ToggleMoveSettingsDetails() => IsMoveSettingsDetailsVisible = !IsMoveSettingsDetailsVisible;

    /// <summary>
    /// 保留移动前的有效设置：写到被移动的子树根上，与移动合为一步撤销；移动后已有其他编辑时不再写入。
    /// </summary>
    [RelayCommand]
    private void KeepMovedSettings()
    {
        if (_pendingMoveSettingsChange is not { } change)
            return;
        var document = change.Node.Document;
        if (!document.CanKeepMovedSettings(change))
            StatusMessage = "移动之后已有其他编辑，无法再保留原值；可以撤销后重新移动。";
        else if (document.KeepMovedSettings(change))
            StatusMessage = $"已在“{change.Node.Name}”上保留移动前的 {change.ChangedKeys.Count} 项设置。";
        else
            StatusMessage = "移动前的设置已由子树内部提供，无需写入。";
        ShowMoveSettingsNotice(null);
    }

    /// <summary>忽略提示，接受新位置的继承。</summary>
    [RelayCommand]
    private void DismissMoveSettingsNotice() => ShowMoveSettingsNotice(null);
}
