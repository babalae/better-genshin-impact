using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class AutoPickBlacklistConfigWindow : FluentWindow
{
    private readonly AutoPickBlacklistConfigViewModel _viewModel;

    public AutoPickBlacklistConfigWindow(AutoPickConfig config)
    {
        _viewModel = new AutoPickBlacklistConfigViewModel(config);
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
