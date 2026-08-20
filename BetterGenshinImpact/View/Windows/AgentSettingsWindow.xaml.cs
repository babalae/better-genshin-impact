using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Pages;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class AgentSettingsWindow : FluentWindow
{
    public AgentPageViewModel ViewModel { get; }

    public AgentSettingsWindow(AgentPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
