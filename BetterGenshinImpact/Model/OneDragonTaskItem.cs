using System;
using System.Threading.Tasks;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
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
            case "领取邮件":
                Action = async () =>
                {
                    await new ClaimMailRewardsTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case "合成树脂":
                Action = async () =>
                {
                    try
                    {
                        await new GoToCraftingBenchTask().Start(config.CraftingBenchCountry,
                            CancellationContext.Instance.Cts.Token);
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

                    var (partyName, domainName, sundaySelectedValue) = config.GetDomainConfig();
                    if (string.IsNullOrEmpty(domainName))
                    {
                        TaskControl.Logger.LogError("一条龙配置内{Msg}需要刷的秘境，跳过", "未选择");
                        return;
                    }
                    else
                    {
                        TaskControl.Logger.LogInformation("自动秘境任务：执行");
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
            case "自动幽境危战":
                Action = async () =>
                {
                    if (string.IsNullOrEmpty(TaskContext.Instance().Config.AutoStygianOnslaughtConfig.StrategyName))
                    {
                        TaskContext.Instance().Config.AutoStygianOnslaughtConfig.StrategyName = "根据队伍自动选择";
                    }

                    var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
                    if (taskSettingsPageViewModel!.GetFightStrategy(TaskContext.Instance().Config.AutoStygianOnslaughtConfig.StrategyName, out var path))
                    {
                        TaskControl.Logger.LogError("自动幽境危战战斗策略{Msg}，跳过", "未配置");
                        return;
                    }

                    await new AutoStygianOnslaughtTask(TaskContext.Instance().Config.AutoStygianOnslaughtConfig, path).Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case "领取每日奖励":
                Action = async () =>
                {
                    await new GoToAdventurersGuildTask().Start(config.AdventurersGuildCountry,
                        CancellationContext.Instance.Cts.Token, config.DailyRewardPartyName);
                    await new ClaimBattlePassRewardsTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case "领取尘歌壶奖励":
                Action = async () =>
                {
                    await new GoToSereniteaPotTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case "自动地脉花":
                Action = async () =>
                {
                    if (!config.ShouldRunLeyLineToday())
                    {
                        TaskControl.Logger.LogInformation("自动地脉花未在运行日期内，跳过");
                        return;
                    }

                    var taskConfig = TaskContext.Instance().Config.AutoLeyLineOutcropConfig;
                    var originalType = taskConfig.LeyLineOutcropType;
                    var originalCountry = taskConfig.Country;
                    var originalCount = taskConfig.Count;
                    var originalExhaustionMode = taskConfig.IsResinExhaustionMode;
                    var originalOpenModeCountMin = taskConfig.OpenModeCountMin;
                    var (type, country) = config.GetLeyLineConfigForToday(taskConfig);

                    try
                    {
                        taskConfig.LeyLineOutcropType = type;
                        taskConfig.Country = country;
                        taskConfig.IsResinExhaustionMode = config.LeyLineResinExhaustionMode;
                        taskConfig.OpenModeCountMin = config.LeyLineOpenModeCountMin;
                        if (config.LeyLineRunCount > 0)
                        {
                            taskConfig.Count = config.LeyLineRunCount;
                        }

                        AutoLeyLineOutcropParam param = new AutoLeyLineOutcropParam();
                        param.SetAutoLeyLineOutcropConfig(taskConfig);
                        await new AutoLeyLineOutcropTask(param, config.LeyLineOneDragonMode)
                            .Start(CancellationContext.Instance.Cts.Token);
                    }
                    finally
                    {
                        taskConfig.LeyLineOutcropType = originalType;
                        taskConfig.Country = originalCountry;
                        taskConfig.Count = originalCount;
                        taskConfig.IsResinExhaustionMode = originalExhaustionMode;
                        taskConfig.OpenModeCountMin = originalOpenModeCountMin;
                    }
                };
                break;
            default:
                Action = () => Task.CompletedTask;
                break;
        }
    }
}
