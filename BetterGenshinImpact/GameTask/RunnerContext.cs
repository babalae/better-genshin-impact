using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 使用 TaskRunner 运行任务时的上下文
/// </summary>
public class RunnerContext : Singleton<RunnerContext>
{
    /// <summary>
    /// 是否是连续执行配置组的场景
    /// </summary>
    public bool IsContinuousRunGroup { get; set; }
    
    public TaskProgress.TaskProgress? taskProgress  { get; set; }
    
    /// <summary>
    /// 暂停逻辑
    /// </summary>
    public bool IsSuspend { get; set; }

    /// <summary>
    /// 优先执行配置组
    /// </summary>
    public bool IsPreExecution { get; set; } = false;
    /// <summary>
    /// 暂停实现
    /// </summary>
    public Dictionary<string, ISuspendable> SuspendableDictionary = new();
    
    /// <summary>
    /// 是否正在自动领取派遣任务
    /// </summary>
    public bool isAutoFetchDispatch { get; set; }

    /// <summary>
    /// 当前使用队伍名称
    /// 游戏内定义的队伍名称
    /// </summary>
    public string? PartyName { get; set; }


    /// <summary>
    /// 自动拾取暂停计数，当大于0时暂停，等于0时不限制。
    /// </summary>
    public int AutoPickTriggerStopCount { get; private set; } = 0;



    /// <summary>
    /// 当前队伍角色信息
    /// </summary>
    private CombatScenes? _combatScenes;
    private readonly object _combatScenesLock = new();
    private readonly List<CombatScenes> _retiredCombatScenes = [];
    private long _combatScenesGeneration;

    /// <summary>
    /// 获取 <c>GetCombatScenes</c> 对应的数据。
    /// </summary>
    public async Task<CombatScenes?> GetCombatScenes(CancellationToken ct)
    {
        long generation;
        lock (_combatScenesLock)
        {
            if (_combatScenes is not null)
            {
                return _combatScenes;
            }

            generation = _combatScenesGeneration;
        }

        // 返回主界面再识别。并发调用允许重复识别，但发布缓存时只保留一个实例，其他候选立即释放。
        var returnMainUiTask = new ReturnMainUiTask();
        await returnMainUiTask.Start(ct);
        await Delay(200, ct);

        using var capture = CaptureToRectArea();
        var detectedScenes = new CombatScenes();
        try
        {
            detectedScenes.InitializeTeam(capture);

            if (!detectedScenes.CheckTeamInitialized())
            {
                Logger.LogError("队伍角色识别失败");
                detectedScenes.Dispose();
                return null;
            }

            lock (_combatScenesLock)
            {
                if (generation != _combatScenesGeneration)
                {
                    // 清理已在异步识别期间发生；不要把过期候选重新发布回缓存。
                    return null;
                }

                if (_combatScenes is null)
                {
                    _combatScenes = detectedScenes;
                    detectedScenes = null!;
                }

                return _combatScenes;
            }
        }
        finally
        {
            // 如果并发调用已先发布缓存，释放本次未采用的候选场景。
            detectedScenes?.Dispose();
        }
    }

    /// <summary>
    /// 尝试静默识别当前队伍信息（无副作用，不出错）
    /// </summary>
    public CombatScenes? TrySyncCombatScenesSilent()
    {
        try
        {
            using var region = CaptureToRectArea();
            var scenes = new CombatScenes(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).InitializeTeamSilent(region);
            if (scenes.CheckTeamInitialized())
            {
                return scenes;
            }
            scenes.Dispose();
        }
        catch
        {
            // 静默模式忽略一切异常
        }
        return null;
    }

    /// <summary>
    /// 停止或重置 <c>ClearCombatScenes</c> 对应的状态。
    /// </summary>
    public void ClearCombatScenes()
    {
        lock (_combatScenesLock)
        {
            _combatScenesGeneration++;
            if (_combatScenes is not null)
            {
                // 调用方拿到的是裸引用；任务进行中只退役不立即 Dispose，避免使现有借用者失效。
                _retiredCombatScenes.Add(_combatScenes);
                _combatScenes = null;
            }
        }
    }

    /// <summary>
    /// 任务结束后的清理
    /// </summary>
    public void Clear()
    {
        // 连续执行配置组的情况下，不清理当前队伍
        if (!IsContinuousRunGroup)
        {
            PartyName = null;
        }

        DisposeCombatScenesCache();
        IsSuspend = false;
        isAutoFetchDispatch = false;
        SuspendableDictionary.Clear();
    }

    /// <summary>
    /// 彻底恢复到初始状态
    /// </summary>
    public void Reset()
    {
        IsContinuousRunGroup = false;
        PartyName = null;
        // Reset 可能在新的启动请求取得 TaskRunner 锁之前被调用。此时旧任务仍可能借用当前场景，
        // 因此这里只退役缓存；持有任务结束时的 Clear 才能安全释放场景及其预测器。
        ClearCombatScenes();
        IsSuspend = false;
        isAutoFetchDispatch = false;
        SuspendableDictionary.Clear();
        AutoPickTriggerStopCount = 0;
        taskProgress = null;
        IsPreExecution = false;
    }

    /// <summary>
    /// 暂停自动拾取，如果传入时间大于0(单位秒)，则在该时间之后自动取消此次暂停（暂停自动拾取计数器-1）,反之暂停拾取（暂停自动拾取计数器+1），此时需要恢复需要手动调用ResumeAutoPick。
    /// </summary>
    public void StopAutoPick(int time = -1)
    {
        AutoPickTriggerStopCount++;
        Logger.LogInformation("暂停自动拾取拾取:"+AutoPickTriggerStopCount);
        ResumeAutoPick(time);
    }
    /// <summary>
    /// 恢复自动拾取（暂停自动拾取计数器-1）。传入参数决定几秒后恢复
    /// </summary>
    public void ResumeAutoPick(int time=0)
    {
        if (time == -1)
        {
            return;
        }

        if (time>0)
        {
            Logger.LogInformation(time+"秒后恢复自动拾取:"+AutoPickTriggerStopCount);
        }
       
        if (time <= 0)
        {
            if (AutoPickTriggerStopCount>0)
            {
                AutoPickTriggerStopCount--;
                Logger.LogInformation("恢复自动拾取:"+AutoPickTriggerStopCount);
            }
        }
        else
        {
            new Thread(() =>
            {
                while (time>0)
                {
                    if (AutoPickTriggerStopCount == 0)
                    {
                        return;
                    }
                    Thread.Sleep(1000);
                    time--;
                }

                ResumeAutoPick();

            }).Start();
        }

    }
    /// <summary>
    /// 在暂停拾取情况下，执行任务
    /// </summary>
    public async Task StopAutoPickRunTask(Func<Task> taskFactory,int time=0)
    {
        try
        {
            AutoPickTriggerStopCount++;
            await taskFactory();
        }
        finally
        {
            ResumeAutoPick(time);
        }

    }
    /// <summary>
    /// 停止或重置 <c>stop</c> 对应的状态。
    /// </summary>
    public void stop()
    {
        DisposeCombatScenesCache();
    }

    /// <summary>
    /// 释放当前实例持有的托管和原生资源。
    /// </summary>
    private void DisposeCombatScenesCache()
    {
        List<CombatScenes> scenes;
        lock (_combatScenesLock)
        {
            _combatScenesGeneration++;
            scenes = [.. _retiredCombatScenes];
            _retiredCombatScenes.Clear();
            if (_combatScenes is not null)
            {
                scenes.Add(_combatScenes);
                _combatScenes = null;
            }
        }

        foreach (var scene in scenes)
        {
            try
            {
                scene.Dispose();
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "释放缓存战斗场景资源失败");
            }
        }
    }
}
