using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

public partial class OneDragonFlowPage
{
    public OneDragonFlowViewModel ViewModel { get; }

    public OneDragonFlowPage(OneDragonFlowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
