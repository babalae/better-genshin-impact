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
using System.Threading;

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
    /// 添加实时任务,会清理之前的所有任务
    /// </summary>
    /// <param name="timer">实时任务触发器</param>
    /// <exception cref="ArgumentNullException"></exception>
    public void AddTimer(RealtimeTimer timer)
    {
        ClearAllTriggers();
        try
        {
            AddTrigger(timer);
        }
        catch (ArgumentException e)
        {
            if (e is ArgumentNullException)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 清理所有实时任务
    /// </summary>
    public void ClearAllTriggers()
    {
        TaskTriggerDispatcher.Instance().ClearTriggers();
    }

    /// <summary>
    /// 添加实时任务,不会清理之前的任务
    /// </summary>
    /// <param name="timer"></param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public void AddTrigger(RealtimeTimer timer)
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

        if (!TaskTriggerDispatcher.Instance().AddTrigger(realtimeTimer.Name, realtimeTimer.Config))
        {
            throw new ArgumentException($"添加实时任务失败: {realtimeTimer.Name}", nameof(realtimeTimer.Name));
        }
    }

    public async Task RunTask(SoloTask soloTask, CancellationTokenSource customCts)
    {
        // 创建链接的取消令牌源，任何一个取消都会触发
        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            customCts.Token,
            CancellationContext.Instance.Cts.Token);
        await RunTask(soloTask, linkedCts.Token);
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
    /// <param name="customCt">自定义取消令牌，允许从JS控制任务取消</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public async Task RunTask(SoloTask soloTask, CancellationToken? customCt = null)
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


        CancellationToken cancellationToken;

        if (customCt != null)
        {
            cancellationToken = customCt.Value;
        }
        else
        {
            // 如果没有自定义令牌，就使用全局令牌
            cancellationToken = CancellationContext.Instance.Cts.Token;
        }
        
        // 根据名称执行任务
        switch (soloTask.Name)
        {
            case "AutoGeniusInvokation":
                if (taskSettingsPageViewModel.GetTcgStrategy(out var content))
                {
                    return;
                }

                await new AutoGeniusInvokationTask(new GeniusInvokationTaskParam(content)).Start(cancellationToken);
                break;

            case "AutoWood":
                await new AutoWoodTask(new WoodTaskParam(taskSettingsPageViewModel.AutoWoodRoundNum,
                    taskSettingsPageViewModel.AutoWoodDailyMaxCount)).Start(cancellationToken);
                break;

            case "AutoFight":
                await new AutoFightHandler().RunAsyncByScript(cancellationToken, null, _config);
                break;

            case "AutoDomain":
                if (taskSettingsPageViewModel.GetFightStrategy(out var path))
                {
                    return;
                }

                await new AutoDomainTask(new AutoDomainParam(0, path)).Start(cancellationToken);
                break;

            case "AutoFishing":
                await new AutoFishingTask(AutoFishingTaskParam.BuildFromSoloTaskConfig(soloTask.Config)).Start(
                    cancellationToken);
                break;

            default:
                throw new ArgumentException($"未知的任务名称: {soloTask.Name}", nameof(soloTask.Name));
        }
    }

    public CancellationTokenSource GetLinkedCancellationTokenSource()
    {
        // 创建一个新的链接令牌源，链接到全局令牌
        return CancellationTokenSource.CreateLinkedTokenSource(CancellationContext.Instance.Cts.Token);
    }


    public CancellationToken GetLinkedCancellationToken()
    {
        return GetLinkedCancellationTokenSource().Token;
    }
}