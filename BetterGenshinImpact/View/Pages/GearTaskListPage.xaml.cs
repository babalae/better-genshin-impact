using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Component;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// GearTaskListPage.xaml 的交互逻辑
/// </summary>
public partial class GearTaskListPage : UserControl
{
    private GearTaskListPageViewModel ViewModel { get; }

    public GearTaskListPage(GearTaskListPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// 任务定义项双击事件
    /// </summary>
    private void TaskDefinitionItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is GearTaskDefinitionViewModel taskDefinition)
        {
            // 双击重命名
            ViewModel.RenameTaskDefinitionCommand.Execute(taskDefinition);
        }
    }
}