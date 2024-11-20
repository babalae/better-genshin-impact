using BetterGenshinImpact.Model;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<OneDragonFlowViewModel> _logger = App.GetLogger<OneDragonFlowViewModel>();

    [ObservableProperty]
    private ObservableCollection<OneDragonTaskItem> _taskList =
    [
        // new OneDragonTaskItem(typeof(MailViewModel)), //领取邮件
        // new OneDragonTaskItem(typeof(CraftViewModel)), // 合成树脂
        // new OneDragonTaskItem(typeof(DailyCommissionViewModel)), // 每日委托
        // new OneDragonTaskItem(typeof(DomainViewModel)), // 自动秘境
        // new OneDragonTaskItem(typeof(ForgingViewModel)), // 自动锻造
        // new OneDragonTaskItem(typeof(LeyLineBlossomViewModel)), // 自动刷地脉花
        // new OneDragonTaskItem(typeof(DailyRewardViewModel)),  // 领取每日奖励
        // new OneDragonTaskItem(typeof(SereniteaPotViewModel)),  // 领取尘歌壶奖励
        // new OneDragonTaskItem(typeof(TcgViewModel)),  // 自动七圣召唤

        new OneDragonTaskItem("领取邮件"),
        new OneDragonTaskItem("合成树脂"),
        // new OneDragonTaskItem("每日委托"),
        new OneDragonTaskItem("自动秘境"),
        // new OneDragonTaskItem("自动锻造"),
        // new OneDragonTaskItem("自动刷地脉花"),
        new OneDragonTaskItem("领取每日奖励"),
        // new OneDragonTaskItem("领取尘歌壶奖励"),
        // new OneDragonTaskItem("自动七圣召唤"),
    ];

    [ObservableProperty]
    private OneDragonTaskItem? _selectedTask;
    
    [ObservableProperty]
    private string _craftingBenchCountry = "枫丹";
    
    [ObservableProperty]
    private string _adventurersGuildCountry = "枫丹";

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }
}
