using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

public partial class TriggerSettingsPage
{
    private TriggerSettingsPageViewModel ViewModel { get; }

    public TriggerSettingsPage(TriggerSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}