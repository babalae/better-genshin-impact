using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Dependence.Model;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoEat;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.ClearScript;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Dispatcher
{
    private readonly ILogger<Dispatcher> _logger = App.GetLogger<Dispatcher>();

    private readonly object _config;

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
    public async Task<object?> RunTask(SoloTask soloTask, CancellationToken? customCt = null)
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
                string content;
                // 检查是否有自定义策略内容  
                if (soloTask.Config != null)
                {
                    var jsObject = (ScriptObject)soloTask.Config;
                    content = ScriptObjectConverter.GetValue(jsObject, "strategy", "");
                    if (string.IsNullOrEmpty(content))
                    {
                        // 回退到原有逻辑  
                        if (taskSettingsPageViewModel.GetTcgStrategy(out content))
                        {
                            return null;
                        }
                    }
                }
                else
                {
                    // 回退到原有逻辑  
                    if (taskSettingsPageViewModel.GetTcgStrategy(out content))
                    {
                        return null;
                    }
                }

                await new AutoGeniusInvokationTask(new GeniusInvokationTaskParam(content)).Start(cancellationToken);
                return null;

            case "AutoWood":
                await new AutoWoodTask(new WoodTaskParam(taskSettingsPageViewModel.AutoWoodRoundNum,
                    taskSettingsPageViewModel.AutoWoodDailyMaxCount)).Start(cancellationToken);
                return null;

            case "AutoFight":
                await new AutoFightHandler().RunAsyncByScript(cancellationToken, null, _config);
                return null;

            case "AutoDomain":
                if (taskSettingsPageViewModel.GetFightStrategy(out var path))
                {
                    return null;
                }

                await new AutoDomainTask(new AutoDomainParam(0, path)).Start(cancellationToken);
                return null;

            case "AutoFishing":
                await new AutoFishingTask(AutoFishingTaskParam.BuildFromSoloTaskConfig(soloTask.Config)).Start(
                    cancellationToken);
                return null;
            case "AutoEat":
                {
                    string? foodName = soloTask.Config == null ? null : ScriptObjectConverter.GetValue((ScriptObject)soloTask.Config, "foodName", (string?)null);
                    FoodEffectType? foodEffectType = soloTask.Config == null ? null : (FoodEffectType?)ScriptObjectConverter.GetValue((ScriptObject)soloTask.Config, "foodEffectType", (int?)null);

                    if (foodName != null && foodEffectType != null)
                    {
                        throw new NotSupportedException("不能同时指定foodName和foodEffectType");
                    }

                    if (foodName == null)
                    {
                        if (foodEffectType != null)
                        {
                            PathingPartyConfig? pathingPartyConfig = _config as PathingPartyConfig;
                            if (pathingPartyConfig == null)
                            {
                                throw new NotSupportedException("foodEffectType参数需要调度器配置，请在调度器下使用");
                            }
                            else
                            {
                                switch (foodEffectType)
                                {
                                    case FoodEffectType.ATKBoostingDish:
                                        foodName = pathingPartyConfig.AutoEatConfig.DefaultAtkBoostingDishName;
                                        if (foodName == null)
                                        {
                                            _logger.LogInformation("缺少{Text}配置，跳过吃Buff", "默认的攻击类料理");
                                            return null;
                                        }
                                        break;
                                    case FoodEffectType.AdventurersDish:
                                        foodName = pathingPartyConfig.AutoEatConfig.DefaultAdventurersDishName;
                                        if (foodName == null)
                                        {
                                            _logger.LogInformation("缺少{Text}配置，跳过吃Buff", "默认的冒险类料理");
                                            return null;
                                        }
                                        break;
                                    case FoodEffectType.DEFBoostingDish:
                                        foodName = pathingPartyConfig.AutoEatConfig.DefaultDefBoostingDishName;
                                        if (foodName == null)
                                        {
                                            _logger.LogInformation("缺少{Text}配置，跳过吃Buff", "默认的防御类料理");
                                            return null;
                                        }
                                        break;
                                    default:
                                        throw new NotSupportedException("JS脚本入参错误：错误的foodEffectType");
                                }
                            }
                        }
                    }

                    var autoEatConfig = TaskContext.Instance().Config.AutoEatConfig;
                    return await new AutoEatTask(new AutoEatParam()
                    {
                        CheckInterval = autoEatConfig.CheckInterval,
                        EatInterval = autoEatConfig.EatInterval,
                        ShowNotification = autoEatConfig.ShowNotification,
                        FoodName = foodName
                    }).Start(cancellationToken);
                }
            case "CountInventoryItem":
                {
                    if (soloTask.Config == null)
                    {
                        throw new NullReferenceException($"{nameof(soloTask.Config)}为空");
                    }
                    GridScreenName gridScreenName = ScriptObjectConverter.GetValue((ScriptObject)soloTask.Config, "gridScreenName", (GridScreenName?)null) ?? throw new Exception("gridScreenName为空或错误");
                    string? itemName = ScriptObjectConverter.GetValue((ScriptObject)soloTask.Config, "itemName", (string?)null);
                    IEnumerable<string>? itemNames = ScriptObjectConverter.GetValue<string>((ScriptObject)soloTask.Config, "itemNames");
                    if (itemName != null && itemNames != null)
                    {
                        throw new ArgumentException($"参数{nameof(itemName)}和{nameof(itemNames)}不能同时使用");
                    }
                    if (itemName == null && itemNames == null)
                    {
                        throw new ArgumentException($"参数{nameof(itemName)}和{nameof(itemNames)}不能同时为空");
                    }
                    var result = await new CountInventoryItem(gridScreenName, itemName, itemNames).Start(cancellationToken);
                    if (itemName != null)
                    {
                        return result;
                    }
                    else
                    {
                        dynamic expando = new ExpandoObject();
                        var expandoDict = (IDictionary<string, object>)expando;
                        foreach (var kvp in (Dictionary<string, int>)result)
                        {
                            expandoDict[kvp.Key] = kvp.Value;
                        }
                        return expandoDict;
                    }
                }
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
    
    /// <summary>  
    /// 运行自动秘境任务
    /// </summary>  
    /// <param name="param">秘境任务参数</param>  
    /// <param name="customCt">自定义取消令牌</param>  
    /// <returns></returns>  
    public async Task RunAutoDomainTask(AutoDomainParam param, CancellationToken? customCt = null)  
    {  
        if (param == null)  
        {  
            throw new ArgumentNullException(nameof(param), "秘境任务参数不能为空");  
        }  
  
        CancellationToken cancellationToken = customCt ?? CancellationContext.Instance.Cts.Token;  
        await new AutoDomainTask(param).Start(cancellationToken);  
    }  
  
    /// <summary>  
    /// 运行自动战斗任务
    /// </summary>  
    /// <param name="param">战斗任务参数</param>  
    /// <param name="customCt">自定义取消令牌</param>  
    /// <returns></returns>  
    public async Task RunAutoFightTask(AutoFightParam param, CancellationToken? customCt = null)  
    {  
        if (param == null)  
        {  
            throw new ArgumentNullException(nameof(param), "战斗任务参数不能为空");  
        }  
  
        CancellationToken cancellationToken = customCt ?? CancellationContext.Instance.Cts.Token;  
        await new AutoFightTask(param).Start(cancellationToken);  
    }
    
    /// <summary>  
    /// 运行自动地脉花任务
    /// </summary>  
    /// <param name="param">自动地脉花任务参数</param>  
    /// <param name="customCt">自定义取消令牌</param>  
    /// <returns></returns>  
    public async Task RunAutoLeyLineOutcropTask(AutoLeyLineOutcropParam param, CancellationToken? customCt = null)  
    {  
        if (param == null)  
        {  
            throw new ArgumentNullException(nameof(param), "自动地脉花任务参数不能为空");  
        }  
  
        CancellationToken cancellationToken = customCt ?? CancellationContext.Instance.Cts.Token;  
        await new AutoLeyLineOutcropTask(param).Start(cancellationToken);  
    }
}
