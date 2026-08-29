using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Pages;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class MusicSettingsWindow : FluentWindow
{
    public MusicSettingsWindow(MusicPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
