using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// JS 脚本多选设置中的一个可选项。
/// </summary>
public partial class PuloniaJsScriptSettingOptionViewModel : ObservableObject
{
    /// <summary>
    /// 选项展示文本及最终写入脚本设置的值。
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 当前选项是否已选中。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 建立一个 JS 脚本多选设置项。
    /// </summary>
    public PuloniaJsScriptSettingOptionViewModel(string value, bool isSelected)
    {
        Value = value;
        _isSelected = isSelected;
    }
}
