using BetterGenshinImpact.GameTask.Common.Resin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>合成浓缩树脂的草稿编辑器，默认数量与保留数量分别保存。</summary>
public partial class CraftResinEditorViewModel : ObservableObject, IResinSettingsDialogContent
{
    /// <summary>是否保留指定数量的原粹树脂。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultMode), nameof(ShowKeepAmount), nameof(Summary), nameof(ValidationMessage), nameof(CanConfirm))]
    private bool _keepResin;
    /// <summary>保留数量；切回默认合成时继续保存此值。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ValidationMessage), nameof(CanConfirm))]
    private int _keepAmount;

    /// <summary>将旧的非正数视为默认合成，不在打开时产生编辑。</summary>
    public CraftResinEditorViewModel(int minResinToKeep)
    {
        _keepResin = minResinToKeep > 0;
        // 预备默认 40 供首次切换；默认合成仍返回 0，也避免隐藏 NumberBox 初始化时将 0 校正成 1。
        _keepAmount = _keepResin ? minResinToKeep : 40;
    }

    /// <summary>按默认数量合成的单选状态，取消选中由另一单选项处理。</summary>
    public bool IsDefaultMode { get => !KeepResin; set { if (value) KeepResin = false; } }
    /// <summary>是否显示保留数量。</summary>
    public bool ShowKeepAmount => KeepResin;
    /// <inheritdoc />
    public string Title => "树脂使用 · 合成浓缩树脂";
    /// <inheritdoc />
    public string Summary => ResinUsageText.CraftSummary(BuildValue());
    /// <inheritdoc />
    public string? ValidationMessage => KeepResin && KeepAmount is < 1 or > 999 ? "保留数量须为 1–999。" : null;
    /// <inheritdoc />
    public bool CanConfirm => ValidationMessage is null;

    /// <summary>首次选择保留时预填 40，已有正数即使无效也保留供用户修正。</summary>
    partial void OnKeepResinChanged(bool value)
    {
        if (value && KeepAmount <= 0) KeepAmount = 40;
    }

    /// <summary>返回当前草稿；默认合成统一写 0。</summary>
    public int BuildValue() => KeepResin ? KeepAmount : 0;
}
