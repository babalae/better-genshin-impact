using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>新一条龙的步骤高级设置窗口，修改即时写入共享文档。</summary>
public partial class PuloniaOneDragonSettingsDialog : FluentWindow
{
    /// <summary>窗口专属的视图模型，只服务打开时的步骤。</summary>
    public PuloniaOneDragonSettingsDialogViewModel ViewModel { get; }

    /// <summary>配置窗口只绑定已明确选择的节点，不建立另一份运行配置。</summary>
    /// <param name="viewModel">步骤高级设置视图模型；窗口关闭时由调用方释放。</param>
    public PuloniaOneDragonSettingsDialog(PuloniaOneDragonSettingsDialogViewModel viewModel)
    {
        ViewModel = viewModel; DataContext = ViewModel; InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
