using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
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
    /// 当前正在执行的脚本项目，用于 JS 的 HTTP、通知等权限校验。
    /// 由 ScriptService.ExecuteProject 写入；Clear / Reset 不会重置它，与迁出 TaskContext 前的行为一致
    /// </summary>
    public ScriptGroupProject? CurrentScriptProject { get; set; }

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
    /// 实时触发器暂停计数（沿用"暂停自动拾取"的名称），大于0时全部实时触发器暂停，等于0时不限制。
    /// 由 TaskTriggerDispatcher 在每帧开始时检查，暂停期间不调用任何触发器的 OnCapture，截图、遮罩跟随等照常。
    /// </summary>
    public int AutoPickTriggerStopCount { get; private set; } = 0;



    /// <summary>
    /// 当前队伍角色信息
    /// </summary>
    private CombatScenes? _combatScenes;

    public Task<CombatScenes?> GetCombatScenes(CancellationToken ct) => GetCombatScenes(ct, null);

    internal async Task<CombatScenes?> GetCombatScenes(CancellationToken ct, InstrumentQuestRecovery? quest)
    {
        if (quest != null)
            await EnsureWaypointActionWorldAsync(ct, quest);

        if (_combatScenes == null)
        {
            if (quest == null)
            {
                await new ReturnMainUiTask().Start(ct);
                await Delay(200, ct);
            }

            using var region = CaptureToRectArea();
            _combatScenes = new CombatScenes().InitializeTeam(region);
            if (!_combatScenes.CheckTeamInitialized())
            {
                Logger.LogError("队伍角色识别失败");
                _combatScenes = null;
            }
        }

        return _combatScenes;
    }

    internal static async Task<bool> TrySwitchWaypointAvatarAsync(Avatar avatar, int tries, CancellationToken ct,
        InstrumentQuestRecovery? quest, bool guardWorld = false)
    {
        if (quest == null && !guardWorld) return avatar.TrySwitch(tries);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            bool interrupted = false;
            bool CanSwitch(ImageRegion screen)
            {
                ct.ThrowIfCancellationRequested();
                interrupted = !IsWaypointActionWorld(screen);
                return !interrupted;
            }
            bool switched = avatar.TrySwitch(tries, CanSwitch);
            if (!interrupted) return switched;
            await EnsureWaypointActionWorldAsync(ct, quest);
        }
    }

    internal static bool IsWaypointActionWorld(ImageRegion screen)
    {
        var view = InstrumentExitRecovery.ReadScreen(screen, true, out var target);
        using (target) return view == InstrumentExitScreen.World;
    }

    /// <returns>等待期间是否离开过大世界，需要调用者重新确认角色或目标。</returns>
    internal static async Task<bool> EnsureWaypointActionWorldAsync(CancellationToken ct, InstrumentQuestRecovery? quest)
    {
        var runtime = TaskContext.Instance().Runtime;
        var interrupted = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (runtime == null || !ReferenceEquals(runtime, TaskContext.Instance().Runtime))
                throw new OperationCanceledException("路线动作绑定的游戏实例已更换", ct);
            using var screen = CaptureToRectArea();
            var view = InstrumentExitRecovery.ReadScreen(screen, true, out var target, classifyMenu: true);
            target?.Dispose();
            if (view == InstrumentExitScreen.World) return interrupted;
            interrupted = true;
            if (view == InstrumentExitScreen.OrdinaryMenu)
            {
                bool closed = await InstrumentExitRecovery.TryCloseOrdinaryMenuAsync(runtime, ct);
                await Delay(closed ? 900 : 100, ct);
                continue;
            }
            if (await InstrumentExitRecovery.TryExitAsync(screen, ct, instrumentOnly: true, quest: quest)) continue;
            if (await InstrumentExitRecovery.WaitForWorldOrMenuAsync(runtime, "等待当前路线动作恢复", ct, quest,
                    retryInitialInstrument: true))
                return true;
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

    public void ClearCombatScenes()
    {
        _combatScenes = null;
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

        _combatScenes = null;
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
        _combatScenes = null;
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
    public void stop()
    {
        _combatScenes = null;
    }
}
