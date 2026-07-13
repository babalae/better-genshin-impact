using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class NotificationSettingsPage : Page
{
    private NotificationSettingsPageViewModel ViewModel { get; }

    public NotificationSettingsPage(NotificationSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
