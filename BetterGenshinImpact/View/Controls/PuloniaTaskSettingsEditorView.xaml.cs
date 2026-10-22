using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 任务设置编辑器视图：优先展示独立任务的专属设置界面，否则展示自动生成的通用设置卡片。
/// DataContext 由宿主绑定为 <see cref="ViewModel.Pages.Pulonia.PuloniaTaskSettingsEditorViewModel"/>。
/// </summary>
public partial class PuloniaTaskSettingsEditorView : UserControl
{
    /// <summary>初始化新增与编辑弹窗共用的任务设置区域。</summary>
    public PuloniaTaskSettingsEditorView() => InitializeComponent();
}
