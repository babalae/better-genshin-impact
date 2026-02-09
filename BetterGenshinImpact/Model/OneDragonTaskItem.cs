using BetterGenshinImpact.Helpers;
﻿using System;
using System.Threading.Tasks;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Model;

public partial class OneDragonTaskItem : ObservableObject
{
    [ObservableProperty] private string _name;

    [ObservableProperty] private Brush _statusColor = Brushes.Gray;

    [ObservableProperty] private bool _isEnabled = true;

    [ObservableProperty] private OneDragonBaseViewModel? _viewModel;

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
            case Lang.S["Gen_12025_21caea"]:
                Action = async () =>
                {
                    await new ClaimMailRewardsTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case Lang.S["OneDragon_005_4762ca"]:
                Action = async () =>
                {
                    try
                    {
                        await new GoToCraftingBenchTask().Start(config.CraftingBenchCountry,
                            CancellationContext.Instance.Cts.Token);
                    }
                    catch (Exception e)
                    {
                        TaskControl.Logger.LogError(Lang.S["Gen_12024_ea0b2f"] + e.Message);
                    }
                };
                break;
            case Lang.S["Task_059_1f7122"]:
                Action = async () =>
                {
                    if (string.IsNullOrEmpty(TaskContext.Instance().Config.AutoFightConfig.StrategyName))
                    {
                        TaskContext.Instance().Config.AutoFightConfig.StrategyName = Lang.S["GameTask_10386_0bfb2b"];
                    }

                    var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
                    if (taskSettingsPageViewModel!.GetFightStrategy(out var path))
                    {
                        TaskControl.Logger.LogError(Lang.S["Gen_12023_ecd8f5"], "未配置");
                        return;
                    }

                    var (partyName, domainName, sundaySelectedValue) = config.GetDomainConfig();
                    if (string.IsNullOrEmpty(domainName))
                    {
                        TaskControl.Logger.LogError(Lang.S["Gen_12021_a28655"], "未选择");
                        return;
                    }
                    else
                    {
                        TaskControl.Logger.LogInformation(Lang.S["Gen_12020_f793b1"]);
                    }

                    var autoDomainParam = new AutoDomainParam(0, path)
                    {
                        PartyName = partyName,
                        DomainName = domainName,
                        SundaySelectedValue = sundaySelectedValue
                    };
                    await new AutoDomainTask(autoDomainParam).Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case Lang.S["Task_085_4fdef3"]:
                Action = async () =>
                {
                    if (string.IsNullOrEmpty(TaskContext.Instance().Config.AutoStygianOnslaughtConfig.StrategyName))
                    {
                        TaskContext.Instance().Config.AutoStygianOnslaughtConfig.StrategyName = Lang.S["GameTask_10386_0bfb2b"];
                    }

                    var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
                    if (taskSettingsPageViewModel!.GetFightStrategy(TaskContext.Instance().Config.AutoStygianOnslaughtConfig.StrategyName, out var path))
                    {
                        TaskControl.Logger.LogError(Lang.S["Gen_12018_9f3c30"], "未配置");
                        return;
                    }

                    await new AutoStygianOnslaughtTask(TaskContext.Instance().Config.AutoStygianOnslaughtConfig, path).Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case Lang.S["Gen_12017_8fdc0b"]:
                Action = async () =>
                {
                    await new GoToAdventurersGuildTask().Start(config.AdventurersGuildCountry,
                        CancellationContext.Instance.Cts.Token, config.DailyRewardPartyName);
                    await new ClaimBattlePassRewardsTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case Lang.S["GameTask_11624_df031f"]:
                Action = async () =>
                {
                    await new GoToSereniteaPotTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            default:
                Action = () => Task.CompletedTask;
                break;
        }
    }
}