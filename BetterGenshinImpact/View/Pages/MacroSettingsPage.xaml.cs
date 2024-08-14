using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// MacroSettingsPage.xaml 的交互逻辑
/// </summary>
public partial class MacroSettingsPage : Page
{
    MacroSettingsPageViewModel ViewModel { get; }

    public MacroSettingsPage(MacroSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}