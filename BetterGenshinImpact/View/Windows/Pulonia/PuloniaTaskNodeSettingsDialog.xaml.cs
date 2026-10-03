using BetterGenshinImpact.ViewModel.Pages;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// Pulonia 通用节点配置弹窗，承接任务树中全部节点类型的再次编辑。
/// </summary>
public partial class PuloniaTaskNodeSettingsDialog : FluentWindow
{
    /// <summary>
    /// 当前任务计划页面视图模型。
    /// </summary>
    public PuloniaTaskPlanViewModel ViewModel { get; }

    /// <summary>
    /// 使用已经选中的任务节点建立配置弹窗。
    /// </summary>
    public PuloniaTaskNodeSettingsDialog(PuloniaTaskPlanViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }
}
