using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class TaskSettingsPage : Page
{
    private TaskSettingsPageViewModel ViewModel { get; }

    public TaskSettingsPage(TaskSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
