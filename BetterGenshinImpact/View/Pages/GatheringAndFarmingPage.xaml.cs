using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class GatheringAndFarmingPage : UserControl
{
    private GatheringAndFarmingPageViewModel ViewModel { get; }

    public GatheringAndFarmingPage(GatheringAndFarmingPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
