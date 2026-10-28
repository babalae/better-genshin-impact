using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 共享设置编辑器视图；DataContext 由宿主窗口设置为 PuloniaSharedSettingsEditorViewModel。
/// </summary>
public partial class PuloniaSharedSettingsEditorView : UserControl
{
    /// <summary>初始化视图。</summary>
    public PuloniaSharedSettingsEditorView()
    {
        InitializeComponent();
    }
}
