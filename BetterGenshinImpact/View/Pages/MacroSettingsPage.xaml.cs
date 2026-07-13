using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class MacroSettingsPage : Page
{
    private MacroSettingsPageViewModel ViewModel { get; }

    public MacroSettingsPage(MacroSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
