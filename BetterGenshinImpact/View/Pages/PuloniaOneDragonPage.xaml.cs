using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>以主题卡片包装 Pulonia 任务计划的新一条龙入口。</summary>
public partial class PuloniaOneDragonPage : UserControl
{
    /// <summary>独立界面包装，共享底层任务计划编辑会话。</summary>
    public PuloniaOneDragonViewModel ViewModel { get; }
    /// <summary>建立新视图，现有任务计划页面继续保留。</summary>
    public PuloniaOneDragonPage(PuloniaOneDragonViewModel viewModel)
    { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
}
