using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// TaskSettingsPage.xaml 的交互逻辑
/// </summary>
public partial class TaskSettingsPage : Page
{

    TaskSettingsPageViewModel ViewModel { get; }

    public TaskSettingsPage(TaskSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
