using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// Pulonia 任务库、资源预览和批量添加入口。
/// </summary>
public partial class PuloniaTaskLibraryPage : UserControl
{
    /// <summary>
    /// 页面使用的任务库视图模型。
    /// </summary>
    public PuloniaTaskLibraryViewModel ViewModel { get; }

    /// <summary>
    /// 建立任务库页面并使用页面自身作为绑定根。
    /// </summary>
    public PuloniaTaskLibraryPage(PuloniaTaskLibraryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
