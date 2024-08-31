using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// JsListPage.xaml 的交互逻辑
/// </summary>
public partial class MapPathingPage : Page
{
    private MapPathingViewModel ViewModel { get; }

    public MapPathingPage(MapPathingViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
