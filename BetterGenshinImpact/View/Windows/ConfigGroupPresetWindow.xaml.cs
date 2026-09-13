using BetterGenshinImpact.Core.Script.Group.Preset;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class ConfigGroupPresetWindow : FluentWindow
{
    public ConfigGroupPresetWindowViewModel ViewModel { get; }

    public ConfigGroupPresetWindow(ConfigGroupPresetWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
