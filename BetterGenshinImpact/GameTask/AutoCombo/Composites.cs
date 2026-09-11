using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using CsTrees;
using CsTrees.Blackboard;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.AutoCombo;

/// <summary>
/// 按时长循环基础动作：切换到目标角色并站场，循环执行基础动作序列直到满指定秒数（最后一个动作可能略微超出）
/// 期间返回 Running，时间到返回 Success，角色不存在返回 Failure
/// </summary>
public partial class BasicActionsByDuration : Composite
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly double _seconds;
    private readonly bool _useE;

    /// <summary>窗口起始时间戳（Stopwatch）；Initialize 时重置</summary>
    private long _startTicks;

    /// <summary>当前轮转到的子动作下标；跨 Tick 保留以支持非阻塞子动作的续跑</summary>
    private int _index;

    private BasicActionsByDuration(string name, string avatarName, double seconds, bool useE, IEnumerable<Behaviour> children) : base(name, children)
    {
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "站场时长必须大于0");
        }

        _avatarName = avatarName;
        _seconds = seconds;
        _useE = useE;

        FeedbackMessage = $"站场{seconds}秒";
    }

    public override async IAsyncEnumerable<Behaviour> Tick()
    {
        // 是否为上个 Tick 未完成的子动作续跑（当前叶子均阻塞式完成，此路径为叶子非阻塞化预留）
        var resuming = false;
        if (Status != Status.Running)
        {
            foreach (var child in Children)
            {
                if (child.Status != Status.Invalid)
                    child.Stop(Status.Invalid);
            }
            _index = 0;
            _startTicks = Stopwatch.GetTimestamp();
            Initialize();
        }
        else if (CurrentChild is { Status: Status.Running })
        {
            resuming = true;
        }

        if (!resuming)
        {
            var avatar = BehaviourHelper.ResolveAvatar(CombatScenes.Get(), _avatarName);
            if (avatar == null)
            {
                Stop(Status.Failure);
                yield return this;
                yield break;
            }

            // 时间到：窗口的正常结束出口；超时判定在子动作边界，最后一个动作完整跑完后结束
            if (Stopwatch.GetTimestamp() - _startTicks > _seconds * Stopwatch.Frequency)
            {
                Stop(Status.Success);
                yield return this;
                yield break;
            }

            // E穿插：每个子动作边界检查一次，就绪则切人释放（UseSkill 内部自动记录冷却）
            if (_useE && ESkillCdTracker.IsReady(avatar.Name))
            {
                avatar.Switch();
                avatar.UseSkill(hold: false);
            }

            FeedbackMessage = $"剩余 {Math.Max(0, _seconds - (Stopwatch.GetTimestamp() - _startTicks) / (double)Stopwatch.Frequency):F1}s";
        }

        // 执行当前子动作；阻塞式叶子在此处同步跑完，非阻塞叶子可能返回 Running 待续跑
        var current = Children[_index];
        CurrentChild = current;
        await foreach (var node in current.Tick())
        {
            yield return node;
        }

        if (current.Status == Status.Running)
        {
            Status = Status.Running;
            yield return this;
            yield break;
        }

        if (current.Status != Status.Success)
        {
            // 非 Success（角色不存在等）真实失败向外传播
            Stop(current.Status);
            yield return this;
            yield break;
        }

        // 轮转推进；Running 让外层记忆 Selector 下个 Tick 续跑窗口
        _index = (_index + 1) % Children.Count;
        Status = Status.Running;
        yield return this;
    }

    /// <summary>
    /// 解除当前正在运行的子树，取 <see cref="Composite.CurrentChild"/> 为目标，完成后置为 Invalid。
    /// </summary>
    public async override IAsyncEnumerable<Behaviour> Handoff()
    {
        if (Status != Status.Running)
            yield break;

        if (CurrentChild is { Status: Status.Running } runner)
        {
            await foreach (var node in runner.Handoff())
                yield return node;
            if (runner.Status == Status.Running)
            {
                // 子树仍在 Handoff 中 → 挂起陪跑
                yield return this;
                yield break;
            }
        }

        Status = Status.Invalid;
        yield return this;
    }
}

/// <summary>
/// 按次数循环基础动作：切换到目标角色并站场，循环执行基础动作序列指定轮数（times 轮完整跑完后结束）
/// 期间返回 Running，轮数完成返回 Success，角色不存在返回 Failure
/// </summary>
public partial class BasicActionsByCount : Composite
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly int _times;
    private readonly bool _useE;

    /// <summary>已完整跑完的轮数</summary>
    private int _completedLoops;

    /// <summary>当前轮转到的子动作下标；跨 Tick 保留以支持非阻塞子动作的续跑</summary>
    private int _index;

    private BasicActionsByCount(string name, string avatarName, int times, bool useE, IEnumerable<Behaviour> children) : base(name, children)
    {
        if (times <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(times), times, "站场轮数必须大于0");
        }

        _avatarName = avatarName;
        _times = times;
        _useE = useE;

        FeedbackMessage = $"站场{times}轮";
    }

    public override async IAsyncEnumerable<Behaviour> Tick()
    {
        // 是否为上个 Tick 未完成的子动作续跑（当前叶子均阻塞式完成，此路径为叶子非阻塞化预留）
        var resuming = false;
        if (Status != Status.Running)
        {
            foreach (var child in Children)
            {
                if (child.Status != Status.Invalid)
                    child.Stop(Status.Invalid);
            }
            _index = 0;
            Initialize();
        }
        else if (CurrentChild is { Status: Status.Running })
        {
            resuming = true;
        }

        if (!resuming)
        {
            var avatar = BehaviourHelper.ResolveAvatar(CombatScenes.Get(), _avatarName);
            if (avatar == null)
            {
                Stop(Status.Failure);
                yield return this;
                yield break;
            }

            // E穿插：每个子动作边界检查一次，就绪则切人释放（UseSkill 内部自动记录冷却）
            if (_useE && ESkillCdTracker.IsReady(avatar.Name))
            {
                avatar.Switch();
                avatar.UseSkill(hold: false);
            }

            FeedbackMessage = $"第 {Math.Min(_completedLoops + 1, _times)}/{_times} 轮";
        }

        // 执行当前子动作；阻塞式叶子在此处同步跑完，非阻塞叶子可能返回 Running 待续跑
        var current = Children[_index];
        CurrentChild = current;
        await foreach (var node in current.Tick())
        {
            yield return node;
        }

        if (current.Status == Status.Running)
        {
            Status = Status.Running;
            yield return this;
            yield break;
        }

        if (current.Status != Status.Success)
        {
            // 非 Success（角色不存在等）真实失败向外传播
            Stop(current.Status);
            yield return this;
            yield break;
        }

        // 轮转推进；Running 让外层记忆 Selector 下个 Tick 续跑窗口
        _index = (_index + 1) % Children.Count;
        if (_index == 0)
        {
            // 一轮完整跑完；达到目标轮数即正常结束
            _completedLoops++;
            if (_completedLoops >= _times)
            {
                Stop(Status.Success);
                yield return this;
                yield break;
            }
        }
        Status = Status.Running;
        yield return this;
    }

    /// <summary>
    /// 解除当前正在运行的子树，取 <see cref="Composite.CurrentChild"/> 为目标，完成后置为 Invalid。
    /// </summary>
    public async override IAsyncEnumerable<Behaviour> Handoff()
    {
        if (Status != Status.Running)
            yield break;

        if (CurrentChild is { Status: Status.Running } runner)
        {
            await foreach (var node in runner.Handoff())
                yield return node;
            if (runner.Status == Status.Running)
            {
                // 子树仍在 Handoff 中 → 挂起陪跑
                yield return this;
                yield break;
            }
        }

        Status = Status.Invalid;
        yield return this;
    }
}
