using System;
using System.Threading.Tasks;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Model;

public partial class OneDragonTaskItem : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private Brush _statusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private OneDragonBaseViewModel? _viewModel;

    public Func<Task>? Action { get; private set; }

    public OneDragonTaskItem(string name)
    {
        Name = name;
    }

    // public OneDragonTaskItem(Type viewModelType, Func<Task> action)
    // {
    //     ViewModel = App.GetService(viewModelType) as OneDragonBaseViewModel;
    //     if (ViewModel == null)
    //     {
    //         throw new ArgumentException("Invalid view model type", nameof(viewModelType));
    //     }
    //     Name = ViewModel.Title;
    //     Action = action;
    // }

    public void InitAction(OneDragonFlowConfig config)
    {
        if (config.TaskEnabledList.TryGetValue(Name, out _))
        {
            config.TaskEnabledList[Name] = IsEnabled;
        }
        else
        {
            config.TaskEnabledList.Add(Name, IsEnabled);
        }

        switch (Name)
        {
            case "领取邮件":
                Action = async () => { await new ClaimMailRewardsTask().Start(CancellationContext.Instance.Cts.Token); };
                break;
            case "合成树脂":
                Action = async () =>
                {
                    try
                    {
                        await new GoToCraftingBenchTask().Start(config.CraftingBenchCountry, CancellationContext.Instance.Cts.Token);
                    }
                    catch (Exception e)
                    {
                        TaskControl.Logger.LogError("合成树脂执行异常：" + e.Message);
                    }
                };
                break;
            case "自动秘境":
                Action = async () =>
                {
                    if (string.IsNullOrEmpty(TaskContext.Instance().Config.AutoFightConfig.StrategyName))
                    {
                        TaskContext.Instance().Config.AutoFightConfig.StrategyName = "根据队伍自动选择";
                    }

                    var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
                    if (taskSettingsPageViewModel!.GetFightStrategy(out var path))
                    {
                        TaskControl.Logger.LogError("自动秘境战斗策略{Msg}，跳过", "未配置");
                        return;
                    }

                    var (partyName, domainName) = config.GetDomainConfig();
                    if (string.IsNullOrEmpty(domainName) || string.IsNullOrEmpty(partyName) || partyName == "地脉花")
                    {
                        TaskControl.Logger.LogError("一条龙配置内{Msg}需要刷的秘境，跳过","未选择");
                        return;
                    }
                    else
                    {
                        TaskControl.Logger.LogInformation("自动秘境任务：执行");
                    }
                    var autoDomainParam = new AutoDomainParam(0, path)
                    {
                        PartyName = partyName,
                        DomainName = domainName
                    };
                    await new AutoDomainTask(autoDomainParam).Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case "领取每日奖励":
                Action = async () =>
                {
                    await new GoToAdventurersGuildTask().Start(config.AdventurersGuildCountry, CancellationContext.Instance.Cts.Token, config.DailyRewardPartyName);
                    await new ClaimBattlePassRewardsTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            default:
                Action = () => Task.CompletedTask;
                break;
        }
    }
}