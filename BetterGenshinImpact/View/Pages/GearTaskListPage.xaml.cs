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


}