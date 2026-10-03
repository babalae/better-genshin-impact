using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// 不依赖计划编辑器的全局 Pulonia 执行记录入口。
/// </summary>
public partial class PuloniaTaskHistoryPage : UserControl
{
    /// <summary>
    /// 共用历史视图模型。
    /// </summary>
    public PuloniaTaskHistoryViewModel ViewModel { get; }

    /// <summary>
    /// 通过依赖注入建立页面，使用页面自身作为绑定根。
    /// </summary>
    public PuloniaTaskHistoryPage(PuloniaTaskHistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
