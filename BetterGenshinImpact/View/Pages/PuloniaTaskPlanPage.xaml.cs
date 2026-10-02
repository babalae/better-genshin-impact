using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// Pulonia 任务计划编辑页面。
/// </summary>
public partial class PuloniaTaskPlanPage : UserControl
{
    /// <summary>
    /// 页面使用的任务计划视图模型。
    /// </summary>
    public PuloniaTaskPlanViewModel ViewModel { get; }

    /// <summary>
    /// 建立页面并使用页面自身作为绑定根，保持 ViewModel 可显式访问。
    /// </summary>
    public PuloniaTaskPlanPage(PuloniaTaskPlanViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
