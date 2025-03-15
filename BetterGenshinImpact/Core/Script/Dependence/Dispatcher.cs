using BetterGenshinImpact.Core.Script.Dependence.Model;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.ViewModel.Pages;
using System;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Dispatcher
{
    private object _config = null;
    public Dispatcher(object config)
    {
        _config = config;
    }
    public void RunTask()
    {
    }

    /// <summary>
    /// 添加实时任务
    /// </summary>
    /// <param name="timer">实时任务触发器</param>
    /// <exception cref="ArgumentNullException"></exception>
    public void AddTimer(RealtimeTimer timer)
    {
        var realtimeTimer = timer;
        if (realtimeTimer == null)
        {
            throw new ArgumentNullException(nameof(realtimeTimer), "实时任务对象不能为空");
        }
        if (string.IsNullOrEmpty(realtimeTimer.Name))
        {
            throw new ArgumentNullException(nameof(realtimeTimer.Name), "实时任务名称不能为空");
        }

        TaskTriggerDispatcher.Instance().AddTrigger(realtimeTimer.Name, realtimeTimer.Config);
    }

    /// <summary>
    /// 运行独立任务
    /// </summary>
    /// <param name="soloTask">
    /// 支持的任务名称:
    /// - AutoGeniusInvokation: 启动自动七圣召唤任务
    /// - AutoWood: 启动自动伐木任务
    /// - AutoFight: 启动自动战斗任务
    /// - AutoDomain: 启动自动秘境任务
    /// </param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public async Task RunTask(SoloTask soloTask)
    {
        if (soloTask == null)
        {
            throw new ArgumentNullException(nameof(soloTask), "独立任务对象不能为空");
        }
        var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
        if (taskSettingsPageViewModel == null)
        {
            throw new ArgumentNullException(nameof(taskSettingsPageViewModel), "内部视图模型对象为空");
        }

        // 根据名称执行任务
        switch (soloTask.Name)
        {
            case "AutoGeniusInvokation":
                if (taskSettingsPageViewModel.GetTcgStrategy(out var content))
                {
                    return;
                }
                await new AutoGeniusInvokationTask(new GeniusInvokationTaskParam(content)).Start(CancellationContext.Instance.Cts.Token);
                break;

            case "AutoWood":
                await new AutoWoodTask(new WoodTaskParam(taskSettingsPageViewModel.AutoWoodRoundNum, taskSettingsPageViewModel.AutoWoodDailyMaxCount)).Start(CancellationContext.Instance.Cts.Token);
                break;

            case "AutoFight":

                /*if (taskSettingsPageViewModel.GetFightStrategy(out var path1))
                {
                    return;
                }
                var autoFightConfig = TaskContext.Instance().Config.AutoFightConfig;
                var param = new AutoFightParam(path1, autoFightConfig);
                await new AutoFightTask(param).Start(CancellationContext.Instance.Cts.Token);*/
                await new AutoFightHandler().RunAsyncByScript(CancellationContext.Instance.Cts.Token, null, _config);
                break;

            case "AutoDomain":
                if (taskSettingsPageViewModel.GetFightStrategy(out var path))
                {
                    return;
                }
                await new AutoDomainTask(new AutoDomainParam(0, path)).Start(CancellationContext.Instance.Cts.Token);
                break;

            // case "AutoMusicGame":
            //     taskSettingsPageViewModel.SwitchAutoMusicGameCommand.Execute(null);
            //     break;

            case "AutoFishing":
                await new AutoFishingTask(AutoFishingTaskParam.BuildFromSoloTaskConfig(soloTask.Config)).Start(CancellationContext.Instance.Cts.Token);
                break;

            default:
                throw new ArgumentException($"未知的任务名称: {soloTask.Name}", nameof(soloTask.Name));
        }
    }
}
