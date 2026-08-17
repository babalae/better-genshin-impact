using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class AutoPickWhitelistConfigWindow : FluentWindow
{
    private readonly AutoPickWhitelistConfigViewModel _viewModel;

    public AutoPickWhitelistConfigWindow(AutoPickConfig config)
    {
        _viewModel = new AutoPickWhitelistConfigViewModel(config);
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.CloseRequested += OnCloseRequested;
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Closed += (_, _) => _viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(bool? dialogResult)
    {
        DialogResult = dialogResult;
    }
}
