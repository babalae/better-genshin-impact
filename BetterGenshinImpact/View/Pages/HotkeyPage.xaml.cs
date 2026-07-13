using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class HotKeyPage : UserControl
{
    private HotKeyPageViewModel ViewModel { get; }

    public HotKeyPage(HotKeyPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
