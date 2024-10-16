using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<OneDragonFlowViewModel> _logger = App.GetLogger<OneDragonFlowViewModel>();

    private ISnackbarService _snackbarService;

    public OneDragonFlowViewModel(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }
}
