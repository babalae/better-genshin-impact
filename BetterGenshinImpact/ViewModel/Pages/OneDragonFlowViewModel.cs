using BetterGenshinImpact.Model;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Enum;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

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

        new OneDragonTaskItem("领取邮件", async () => { await Task.Delay(100); }),
        new OneDragonTaskItem("合成树脂", async () =>
        {
            await new GoToCraftingBenchTask()
                .Start("枫丹", CancellationContext.Instance.Cts.Token);
        }),
        // new OneDragonTaskItem("每日委托"),
        new OneDragonTaskItem("自动秘境", async () =>
        {
            var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
            if (taskSettingsPageViewModel!.GetFightStrategy(out var path))
            {
                Logger.LogInformation("自动秘境战斗策略未配置，跳过");
                return;
            }

            await new AutoDomainTask(new AutoDomainParam(0, path)).Start(CancellationContext.Instance.Cts.Token);
        }),
        // new OneDragonTaskItem("自动锻造"),
        // new OneDragonTaskItem("自动刷地脉花"),
        new OneDragonTaskItem("领取每日奖励", async () =>
        {
            // 冒险者工会
            await new GoToAdventurersGuildTask()
                .Start("枫丹", CancellationContext.Instance.Cts.Token);
            // 领取纪行奖励
            await new ClaimBattlePassRewardsTask().Start(CancellationContext.Instance.Cts.Token);
        }),
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

    [RelayCommand]
    private async Task OnOneKeyExecute()
    {
        await new TaskRunner(DispatcherTimerOperationEnum.UseSelfCaptureImage)
            .RunAsync(async () =>
            {
                foreach (var task in TaskList)
                {
                    await task.Action();
                    await Task.Delay(1000);
                }
            });
    }
}