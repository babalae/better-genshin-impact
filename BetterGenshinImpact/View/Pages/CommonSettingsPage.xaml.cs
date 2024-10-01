using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class CommonSettingsPage : Page
{
    private CommonSettingsPageViewModel ViewModel { get; }

    public CommonSettingsPage(CommonSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
