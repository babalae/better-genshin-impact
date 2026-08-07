using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly object _suspendableLock = new();
    private readonly Dictionary<string, List<SuspendableEntry>> _suspendables = new();

    private sealed class SuspendableEntry
    {
        public required Guid Id { get; init; }
        public required ISuspendable Suspendable { get; init; }
    }

    private sealed class SuspendableRegistration : IDisposable
    {
        private RunnerContext? _owner;
        private readonly string _key;
        private readonly Guid _id;

        public SuspendableRegistration(RunnerContext owner, string key, Guid id)
        {
            _owner = owner;
            _key = key;
            _id = id;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.UnregisterSuspendable(_key, _id);
        }
    }

    /// <summary>
    /// 在当前任务生命周期内注册暂停对象。相同键允许嵌套，释放内层注册后会自动恢复外层对象。
    /// </summary>
    public IDisposable RegisterSuspendable(string key, ISuspendable suspendable)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(suspendable);

        var entry = new SuspendableEntry { Id = Guid.NewGuid(), Suspendable = suspendable };
        lock (_suspendableLock)
        {
            if (!_suspendables.TryGetValue(key, out var entries))
            {
                entries = new List<SuspendableEntry>();
                _suspendables.Add(key, entries);
            }

            entries.Add(entry);
        }

        return new SuspendableRegistration(this, key, entry.Id);
    }

    /// <summary>
    /// 注册与 RunnerContext 同生命周期的暂停对象。
    /// </summary>
    public void SetSuspendable(string key, ISuspendable suspendable)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(suspendable);

        lock (_suspendableLock)
        {
            _suspendables[key] =
            [
                new SuspendableEntry { Id = Guid.NewGuid(), Suspendable = suspendable }
            ];
        }
    }

    /// <summary>
    /// 获取当前生效暂停对象的稳定快照，避免暂停过程中集合被修改。
    /// </summary>
    public IReadOnlyList<ISuspendable> GetSuspendablesSnapshot()
    {
        lock (_suspendableLock)
        {
            return _suspendables.Values
                .Where(entries => entries.Count > 0)
                .Select(entries => entries[^1].Suspendable)
                .ToArray();
        }
    }

    private void UnregisterSuspendable(string key, Guid id)
    {
        lock (_suspendableLock)
        {
            if (!_suspendables.TryGetValue(key, out var entries))
            {
                return;
            }

            entries.RemoveAll(entry => entry.Id == id);
            if (entries.Count == 0)
            {
                _suspendables.Remove(key);
            }
        }
    }

    private void ClearSuspendables()
    {
        lock (_suspendableLock)
        {
            _suspendables.Clear();
        }
    }
    
    /// <summary>
    /// 是否正在自动领取派遣任务
    /// </summary>
    private int _isAutoFetchDispatch;

    /// <summary>
    /// 是否正在自动领取派遣任务
    /// </summary>
    public bool isAutoFetchDispatch
    {
        get => Interlocked.CompareExchange(ref _isAutoFetchDispatch, 0, 0) != 0;
        set => Interlocked.Exchange(ref _isAutoFetchDispatch, value ? 1 : 0);
    }

    public bool TryBeginAutoFetchDispatch()
    {
        return Interlocked.CompareExchange(ref _isAutoFetchDispatch, 1, 0) == 0;
    }

    public void EndAutoFetchDispatch()
    {
        Interlocked.Exchange(ref _isAutoFetchDispatch, 0);
    }

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

    public async Task<CombatScenes?> GetCombatScenes(CancellationToken ct)
    {
        if (_combatScenes == null)
        {
            // 返回主界面再识别
            var returnMainUiTask = new ReturnMainUiTask();
            await returnMainUiTask.Start(ct);

            await Delay(200, ct);

            _combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (!_combatScenes.CheckTeamInitialized())
            {
                Logger.LogError("队伍角色识别失败");
                _combatScenes = null;
            }
        }

        return _combatScenes;
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
        ClearSuspendables();
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
        ClearSuspendables();
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
