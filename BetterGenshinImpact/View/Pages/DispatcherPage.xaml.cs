using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

public partial class DispatcherPage
{
    public DispatcherPageViewModel ViewModel { get; }

    public DispatcherPage(DispatcherPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
