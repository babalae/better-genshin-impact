using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Media;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<OneDragonFlowViewModel> _logger = App.GetLogger<OneDragonFlowViewModel>();

    [ObservableProperty]
    private ObservableCollection<OneDragonTaskItem> _taskList = [
        new OneDragonTaskItem { Name = "登录游戏", StatusColor = Brushes.Gray, IsEnabled = true, ViewModel = new LoginConfigViewModel()},
        new OneDragonTaskItem { Name = "合成树脂", StatusColor = Brushes.Gray, IsEnabled = true, ViewModel = new MailConfigViewModel()},
    ];

    [ObservableProperty]
    private OneDragonTaskItem _selectedTask;

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }
}
