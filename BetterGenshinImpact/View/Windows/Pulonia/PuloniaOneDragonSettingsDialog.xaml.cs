using BetterGenshinImpact.ViewModel.Pages;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>新一条龙的独立配置视图，使用共享文档和参数编辑命令。</summary>
public partial class PuloniaOneDragonSettingsDialog : FluentWindow
{
    /// <summary>独立 UI 包装，通过 Editor 访问共享编辑会话。</summary>
    public PuloniaOneDragonViewModel ViewModel { get; }
    /// <summary>配置窗口只绑定已明确选择的节点，不建立另一份运行配置。</summary>
    public PuloniaOneDragonSettingsDialog(PuloniaOneDragonViewModel viewModel)
    { ViewModel = viewModel; DataContext = ViewModel; InitializeComponent(); }
}
