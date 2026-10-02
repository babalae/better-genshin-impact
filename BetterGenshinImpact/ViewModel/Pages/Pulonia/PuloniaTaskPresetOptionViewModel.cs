namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 参数编辑器中的共享预设选项，空 ID 表示不使用预设。
/// </summary>
public sealed class PuloniaTaskPresetOptionViewModel
{
    /// <summary>
    /// 预设稳定 ID；null 表示无预设。
    /// </summary>
    public string? Id { get; }

    /// <summary>
    /// 下拉框显示名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 建立一个预设选择项。
    /// </summary>
    public PuloniaTaskPresetOptionViewModel(string? id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}
