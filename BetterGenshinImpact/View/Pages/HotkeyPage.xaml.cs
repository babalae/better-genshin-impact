using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// TaskSettingsPage.xaml 的交互逻辑
/// </summary>
public partial class HotKeyPage : Page
{
    private HotKeyPageViewModel ViewModel { get; }

    public HotKeyPage(HotKeyPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
