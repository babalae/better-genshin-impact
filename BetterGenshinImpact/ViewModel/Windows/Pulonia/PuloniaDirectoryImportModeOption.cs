using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 创建弹窗中一种目录添加方式。
/// </summary>
public sealed class PuloniaDirectoryImportModeOption
{
    /// <summary>
    /// 稳定模式键，直接传给创建服务。
    /// </summary>
    public PuloniaTaskDirectoryImportMode Mode { get; }

    /// <summary>
    /// 用户可见名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 保存与后续编辑行为说明。
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 建立一种目录添加方式。
    /// </summary>
    public PuloniaDirectoryImportModeOption(PuloniaTaskDirectoryImportMode mode, string displayName, string description)
    {
        Mode = mode;
        DisplayName = displayName;
        Description = description;
    }
}
