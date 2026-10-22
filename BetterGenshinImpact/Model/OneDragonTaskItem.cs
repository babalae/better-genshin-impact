using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Model;

public partial class OneDragonTaskItem : ObservableObject
{
    [ObservableProperty] private string _name;

    [ObservableProperty] private string _id = Guid.NewGuid().ToString();

    [ObservableProperty] private Brush _statusColor = Brushes.Gray;

    [ObservableProperty] private bool _isEnabled = true;

    [ObservableProperty] private bool _isNextTask = false;

    [ObservableProperty] private OneDragonBaseViewModel? _viewModel;

    /// <summary>
    /// 使用一条龙顶层任务的运行环境与令牌执行当前项目。
    /// 任务在拿到运行环境之后才在委托内部构造
    /// </summary>
    public Func<GameRuntime, CancellationToken, Task>? Action { get; private set; }

    public OneDragonTaskItem(string name)
    {
        Name = name;
    }

    public OneDragonTaskItem(string name, string id) : this(name)
    {
        _id = id;
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

    /// <summary>
    /// 按任务名生成执行委托
    /// </summary>
    /// <param name="allConfig">配置根，由创建方（一条龙页面）传入，交给委托内构造的任务</param>
    /// <param name="config">当前一条龙配置</param>
    public void InitAction(AllConfig allConfig, OneDragonFlowConfig config)
    {
        if (config.TaskEnabledList.ContainsKey(Id))
        {
            config.TaskEnabledList[Id] = IsEnabled;
        }
        else
        {
            config.TaskEnabledList.Add(Id, IsEnabled);
            config.TaskDefinitions[Id] = Name;
        }

        switch (Name)
        {
            case "领取邮件":
                Action = async (runtime, ct) =>
                {
                    try
                    {
                        LogIfFailed(Name, await new ClaimMailRewardsTask(runtime).RunAsync(NoParam.Instance, ct));
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        TaskControl.Logger.LogError("领取邮件执行异常：" + e.Message);
                    }
                };
                break;
            case "合成树脂":
                Action = async (runtime, ct) =>
                {
                    try
                    {
                        var param = new CraftCondensedResinParam
                        {
                            Country = config.CraftingBenchCountry,
                            MinResinToKeep = config.MinResinToKeep
                        };
                        LogIfFailed(Name, await new GoToCraftingBenchTask(runtime, allConfig).RunAsync(param, ct));
                    }
                    catch (Exception e)
                    {
                        TaskControl.Logger.LogError("合成树脂执行异常：" + e.Message);
                    }
                };
                break;
            case "自动秘境":
                Action = async (runtime, ct) =>
                {
                    if (string.IsNullOrEmpty(allConfig.AutoFightConfig.StrategyName))
                    {
                        allConfig.AutoFightConfig.StrategyName = "根据队伍自动选择";
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
                    LogIfFailed(Name, await new AutoDomainTask(runtime, allConfig).RunAsync(autoDomainParam, ct));
                };
                break;
            case "自动首领讨伐":
                Action = async (runtime, ct) =>
                {
                    var totalRunCountLimited = config.AutoBossSpecifyRunCount && config.AutoBossTotalRunCountLimit > 0;
                    var remainingTotalRunCount = config.AutoBossTotalRunCountLimit - config.AutoBossCompletedRunCount;
                    if (totalRunCountLimited && remainingTotalRunCount <= 0)
                    {
                        TaskControl.Logger.LogInformation(
                            "自动首领讨伐：累计成功次数已达到上限 {Limit}，跳过",
                            config.AutoBossTotalRunCountLimit);
                        return;
                    }

                    if (string.IsNullOrEmpty(config.AutoBossStrategyName))
                    {
                        config.AutoBossStrategyName = "根据队伍自动选择";
                    }

                    var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
                    if (taskSettingsPageViewModel!.GetFightStrategy(config.AutoBossStrategyName, out var path))
                    {
                        TaskControl.Logger.LogError("自动首领讨伐战斗策略{Msg}，跳过", "未配置");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(config.AutoBossName))
                    {
                        TaskControl.Logger.LogError("一条龙配置内{Msg}需要讨伐的首领，跳过", "未选择");
                        return;
                    }

                    AutoBossParam param = AutoBossParam.CreateWithoutDefaultConfig(path);
                    param.BossName = config.AutoBossName;
                    param.StrategyName = config.AutoBossStrategyName;
                    param.TeamName = config.AutoBossTeamName;
                    param.SpecifyRunCount = config.AutoBossSpecifyRunCount;
                    param.RunCount = totalRunCountLimited
                        ? config.AutoBossSpecifyRunCount
                            ? Math.Min(config.AutoBossRunCount, remainingTotalRunCount)
                            : remainingTotalRunCount
                        : config.AutoBossRunCount;
                    param.UseTransientResin = config.AutoBossUseTransientResin;
                    param.UseFragileResin = config.AutoBossUseFragileResin;
                    param.ReviveRetryCount = config.AutoBossReviveRetryCount;
                    param.ReturnToStatueAfterEachRound = config.AutoBossReturnToStatueAfterEachRound;
                    param.RewardRecognitionEnabled = config.AutoBossRewardRecognitionEnabled;
                    param.Timeout = config.AutoBossTimeout;
                    if (totalRunCountLimited)
                    {
                        param.RewardClaimedCallback = () => config.AutoBossCompletedRunCount++;
                    }
                    LogIfFailed(Name, await new AutoBossTask(runtime, allConfig).RunAsync(param, ct));
                };
                break;
            case "自动幽境危战":
                Action = async (runtime, ct) =>
                {
                    if (string.IsNullOrEmpty(allConfig.AutoStygianOnslaughtConfig.StrategyName))
                    {
                        allConfig.AutoStygianOnslaughtConfig.StrategyName = "根据队伍自动选择";
                    }

                    var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
                    if (taskSettingsPageViewModel!.GetFightStrategy(allConfig.AutoStygianOnslaughtConfig.StrategyName, out var path))
                    {
                        TaskControl.Logger.LogError("自动幽境危战战斗策略{Msg}，跳过", "未配置");
                        return;
                    }

                    AutoStygianOnslaughtParam param = new AutoStygianOnslaughtParam();
                    param.SetAutoStygianOnslaughtConfig(allConfig.AutoStygianOnslaughtConfig);
                    param.CombatScriptBagPath = path;
                    LogIfFailed(Name, await new AutoStygianOnslaughtTask(runtime, allConfig).RunAsync(param, ct));
                };
                break;
            case "领取每日奖励":
                Action = async (runtime, ct) =>
                {
                    await new GoToAdventurersGuildTask(runtime, allConfig).Start(config.AdventurersGuildCountry,
                        ct, config.DailyRewardPartyName);
                    try
                    {
                        LogIfFailed("领取纪行奖励",
                            await new ClaimBattlePassRewardsTask(runtime, allConfig).RunAsync(NoParam.Instance, ct));
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        TaskControl.Logger.LogError("领取纪行奖励执行异常：" + e.Message);
                    }
                };
                break;
            case "领取尘歌壶奖励":
                Action = async (runtime, ct) =>
                {
                    try
                    {
                        var param = new SereniteaPotParam
                        {
                            TeleportType = config.SereniteaPotTpType,
                            // 复制一份，避免任务运行时与一条龙配置共享同一个列表
                            SecretTreasureObjects = [.. config.SecretTreasureObjects]
                        };
                        LogIfFailed(Name, await new GoToSereniteaPotTask(runtime, allConfig).RunAsync(param, ct));
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        TaskControl.Logger.LogError("领取尘歌壶奖励执行异常：" + e.Message);
                    }
                };
                break;
            case "自动地脉花":
                Action = async (runtime, ct) =>
                {
                    if (!config.ShouldRunLeyLineToday())
                    {
                        TaskControl.Logger.LogInformation("自动地脉花未在运行日期内，跳过");
                        return;
                    }

                    // 先从全局地脉花配置生成参数，再用一条龙配置覆盖当天的类型、地区等字段；不修改全局配置
                    var taskConfig = allConfig.AutoLeyLineOutcropConfig;
                    var (type, country) = config.GetLeyLineConfigForToday(taskConfig);
                    var param = new AutoLeyLineOutcropParam();
                    param.SetAutoLeyLineOutcropConfig(taskConfig);
                    param.LeyLineOutcropType = type;
                    param.Country = country;
                    param.IsResinExhaustionMode = config.LeyLineResinExhaustionMode;
                    param.OpenModeCountMin = config.LeyLineOpenModeCountMin;
                    if (config.LeyLineRunCount > 0)
                    {
                        param.Count = config.LeyLineRunCount;
                    }

                    param.OneDragonMode = config.LeyLineOneDragonMode;
                    LogIfFailed(Name, await new AutoLeyLineOutcropTask(runtime, allConfig).RunAsync(param, ct));
                };
                break;
            default:
                Action = (_, _) => Task.CompletedTask;
                break;
        }
    }

    /// <summary>
    /// 任务返回失败结果时输出错误日志；一条龙遇到单个任务失败时继续执行后续任务
    /// </summary>
    /// <param name="taskName">日志中显示的任务名</param>
    /// <param name="result">任务结果</param>
    private static void LogIfFailed(string taskName, ISoloTaskResult result)
    {
        if (result.Kind == SoloTaskResultKind.Failed)
        {
            TaskControl.Logger.LogError("{Name}失败：{Message}", taskName, result.Message);
        }
    }
}
