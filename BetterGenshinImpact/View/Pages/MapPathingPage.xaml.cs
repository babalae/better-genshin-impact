using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class MapPathingPage : UserControl
{
    private MapPathingViewModel ViewModel { get; }

    public MapPathingPage(MapPathingViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
