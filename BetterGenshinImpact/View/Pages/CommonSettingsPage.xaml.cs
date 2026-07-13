using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;
using System.Windows.Input;
using BetterGenshinImpact.View.Windows;

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
