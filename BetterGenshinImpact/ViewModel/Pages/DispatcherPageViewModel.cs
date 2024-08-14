using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class DispatcherPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<DispatcherPageViewModel> _logger = App.GetLogger<DispatcherPageViewModel>();

    private ISnackbarService _snackbarService;

    public DispatcherPageViewModel(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public void OnStartRecord()
    {
    }
}
