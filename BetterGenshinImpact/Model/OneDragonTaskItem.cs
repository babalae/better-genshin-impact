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
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Model;

public partial class OneDragonTaskItem : ObservableObject
{
    [ObservableProperty] private string _name;

    [ObservableProperty] private Brush _statusColor = Brushes.Gray;

    [ObservableProperty] private bool _isEnabled = true;

    [ObservableProperty] private OneDragonBaseViewModel? _viewModel;

    public Func<Task>? Action { get; private set; }
    
    private readonly ILocalizationService _localizationService;

    public OneDragonTaskItem(string name)
    {
        Name = name;
        _localizationService = App.GetService<ILocalizationService>();
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
            case var name when name == _localizationService.GetString("oneDragon.task.claimMail"):
                Action = async () =>
                {
                    await new ClaimMailRewardsTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case var name when name == _localizationService.GetString("oneDragon.task.synthesizeResin"):
                Action = async () =>
                {
                    try
                    {
                        await new GoToCraftingBenchTask().Start(config.CraftingBenchCountry,
                            CancellationContext.Instance.Cts.Token);
                    }
                    catch (Exception e)
                    {
                        TaskControl.Logger.LogError(_localizationService.GetString("oneDragon.error.synthesizeResinException") + e.Message);
                    }
                };
                break;
            case var name when name == _localizationService.GetString("oneDragon.task.autoDomain"):
                Action = async () =>
                {
                    if (string.IsNullOrEmpty(TaskContext.Instance().Config.AutoFightConfig.StrategyName))
                    {
                        TaskContext.Instance().Config.AutoFightConfig.StrategyName = _localizationService.GetString("oneDragon.autoFight.autoSelectByTeam");
                    }

                    var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
                    if (taskSettingsPageViewModel!.GetFightStrategy(out var path))
                    {
                        TaskControl.Logger.LogError(_localizationService.GetString("oneDragon.error.autoDomainStrategyNotConfigured"), _localizationService.GetString("oneDragon.error.notConfigured"));
                        return;
                    }

                    var (partyName, domainName, sundaySelectedValue) = config.GetDomainConfig();
                    if (string.IsNullOrEmpty(domainName))
                    {
                        TaskControl.Logger.LogError(_localizationService.GetString("oneDragon.error.domainNotSelected"), _localizationService.GetString("oneDragon.error.notSelected"));
                        return;
                    }
                    else
                    {
                        TaskControl.Logger.LogInformation(_localizationService.GetString("oneDragon.info.autoDomainTaskExecuting"));
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
            case var name when name == _localizationService.GetString("oneDragon.task.claimDailyRewards"):
                Action = async () =>
                {
                    await new GoToAdventurersGuildTask().Start(config.AdventurersGuildCountry,
                        CancellationContext.Instance.Cts.Token, config.DailyRewardPartyName);
                    await new ClaimBattlePassRewardsTask().Start(CancellationContext.Instance.Cts.Token);
                };
                break;
            case var name when name == _localizationService.GetString("oneDragon.task.claimSereniteaPotRewards"):
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