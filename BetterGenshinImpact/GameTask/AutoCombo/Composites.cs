using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees;
using CsTrees.Blackboard;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.AutoCombo;

/// <summary>
/// 起手技 + 站场窗口的组合基类：先检查并释放一次起手技能（未就绪整体返回 Failure），
/// 就绪则释放后站场轮转执行子动作，直到站场时长或轮数满足
/// </summary>
public abstract class UseEQThenDoActions : Composite
{
    protected readonly Avatar _avatar;

    /// <summary>站场时长（秒）；轮数模式下为 0</summary>
    private readonly double _seconds;

    /// <summary>站场轮数；时长模式下为 0</summary>
    private readonly int _times;

    /// <summary>已完整跑完的轮数（轮数模式）</summary>
    private int _completedLoops;

    /// <summary>窗口起始时间戳（Stopwatch）；整体重启时重置（时长模式）</summary>
    private long _startTicks;

    /// <summary>当前轮转到的子动作下标；跨 Tick 保留以支持非阻塞子动作的续跑</summary>
    private int _index;

    /// <summary>起手技是否已释放；窗口被打断重进时不重复释放，整体重启时重置</summary>
    private bool _leadDone;

    /// <summary>
    /// 黑板键：标记"该角色正在 UseEQ 流程中"。
    /// </summary>
    public abstract BehaviourKeyAccess<Avatar> LeadCastingAvatar { get; set; }

    protected UseEQThenDoActions(string name, Avatar avatar, double seconds, int times, IEnumerable<Behaviour> children) : base(name, children)
    {
        _avatar = avatar;
        _seconds = seconds;
        _times = times;

        FeedbackMessage = times > 0 ? $"站场{times}轮" : $"站场{seconds}秒";
    }

    /// <summary>检查并释放一次起手技能；未就绪返回 false（整体 Failure，不切人、不按键）</summary>
    protected abstract bool CastUseEQ();

    protected override void Initialize()
    {
        // 整体重启（首跑或完全结束后再跑）：重置起手技阶段与子动作状态，下次 Tick 重新检查并释放起手技
        _leadDone = false;
        _index = 0;
        _completedLoops = 0;
        _startTicks = Stopwatch.GetTimestamp();
        // 标记"该角色正在 UseEQ 流程中"，子节点链读到匹配将判 02/03 就绪
        LeadCastingAvatar.Set(_avatar);
    }

    protected override void Terminate(Status newStatus)
    {
        LeadCastingAvatar.Unset();
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
            Initialize();
        }
        else if (CurrentChild is { Status: Status.Running })
        {
            resuming = true;
        }

        if (!resuming)
        {
            if (!_leadDone)
            {
                if (!CastUseEQ())
                {
                    Stop(Status.Failure);
                    yield return this;
                    yield break;
                }
                _leadDone = true;
            }

            if (_times > 0)
            {
                FeedbackMessage = $"第 {Math.Min(_completedLoops + 1, _times)}/{_times} 轮";
            }
            else
            {
                // 时间到：窗口的正常结束出口；超时判定在子动作边界，最后一个动作完整跑完后结束
                if (Stopwatch.GetTimestamp() - _startTicks > _seconds * Stopwatch.Frequency)
                {
                    Stop(Status.Success);
                    yield return this;
                    yield break;
                }

                FeedbackMessage = $"剩余 {Math.Max(0, _seconds - (Stopwatch.GetTimestamp() - _startTicks) / (double)Stopwatch.Frequency):F1}s";
            }
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

        // 子动作 Failure 视为跳过，窗口继续轮转，不会因此中止
        // 轮转推进；Running 让外层记忆 Selector 下个 Tick 续跑窗口
        _index = (_index + 1) % Children.Count;
        if (_times > 0 && _index == 0)
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
    /// 打断重进时整体重启（计时/轮数重置），起手技因 _leadDone 保留不会重复释放。
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
/// 战技就绪则起手后按时长站场：E 就绪则释放一次并站场循环子动作指定秒数，冷却中整体返回 Failure
/// 期间返回 Running，时间到返回 Success
/// </summary>
public partial class UseSkillIfReadyThenDoActionsByDuration : UseEQThenDoActions
{
    private readonly bool _hold;

    [BlackboardKey(Access = Access.Write)]
    public override BehaviourKeyAccess<Avatar> LeadCastingAvatar { get; set; } = null!;

    private UseSkillIfReadyThenDoActionsByDuration(string name, Avatar avatar, bool hold, double seconds, IEnumerable<Behaviour> children)
        : base(name, avatar, seconds, 0, children)
    {
        _hold = hold;
    }

    protected override bool CastUseEQ()
    {
        var state = _avatar.GetSkillCdState();
        if (state == SkillCdState.Unknown)
        {
            // E 冷却图标只显示当前场上角色，切人后让视觉判定生效（GetSkillCdState 内部已含 OCR 兜底）
            _avatar.Switch();
            state = _avatar.GetSkillCdState();
        }

        // 只有 Ready 才释放，Unknown/Cooldown 都视为未就绪
        if (state != SkillCdState.Ready)
        {
            return false;
        }

        _avatar.Switch();
        _avatar.UseSkill(_hold);
        return true;
    }
}

/// <summary>
/// 战技就绪则起手后按次数站场：E 就绪则释放一次并站场循环子动作指定轮数，冷却中整体返回 Failure
/// 期间返回 Running，轮数完成返回 Success
/// </summary>
public partial class UseSkillIfReadyThenDoActionsByCount : UseEQThenDoActions
{
    private readonly bool _hold;

    [BlackboardKey(Access = Access.Write)]
    public override BehaviourKeyAccess<Avatar> LeadCastingAvatar { get; set; } = null!;

    private UseSkillIfReadyThenDoActionsByCount(string name, Avatar avatar, bool hold, int times, IEnumerable<Behaviour> children)
        : base(name, avatar, 0, times, children)
    {
        _hold = hold;
    }

    protected override bool CastUseEQ()
    {
        var state = _avatar.GetSkillCdState();
        if (state == SkillCdState.Unknown)
        {
            // E 冷却图标只显示当前场上角色，切人后让视觉判定生效（GetSkillCdState 内部已含 OCR 兜底）
            _avatar.Switch();
            state = _avatar.GetSkillCdState();
        }

        // 只有 Ready 才释放，Unknown/Cooldown 都视为未就绪
        if (state != SkillCdState.Ready)
        {
            return false;
        }

        _avatar.Switch();
        _avatar.UseSkill(_hold);
        return true;
    }
}

/// <summary>
/// 爆发就绪则起手后按时长站场：Q 就绪则释放一次并站场循环子动作指定秒数，未就绪整体返回 Failure
/// 期间返回 Running，时间到返回 Success
/// </summary>
public partial class UseBurstIfReadyThenDoActionsByDuration : UseEQThenDoActions
{
    [BlackboardKey(Access = Access.Write)]
    public override BehaviourKeyAccess<Avatar> LeadCastingAvatar { get; set; } = null!;

    private UseBurstIfReadyThenDoActionsByDuration(string name, Avatar avatar, double seconds, IEnumerable<Behaviour> children)
        : base(name, avatar, seconds, 0, children)
    {
    }

    protected override bool CastUseEQ()
    {
        // 未就绪直接 Failure，不切人、不产生无效按键
        if (!BehaviourHelper.CheckBurstReady(_avatar, out _))
        {
            return false;
        }

        // 先复位该角色的就绪缓存，后续 IsBurstReady 将重新检测
        _avatar.IsBurstReady = false;

        // TODO: Avatar.UseBurst 内部仍会自行检测就绪状态；待其支持信任外部就绪状态后，
        // 可信任 CheckBurstReady 的就绪结果跳过这次复检
        _avatar.Switch();
        _avatar.UseBurst();
        return true;
    }
}

/// <summary>
/// 爆发就绪则起手后按次数站场：Q 就绪则释放一次并站场循环子动作指定轮数，未就绪整体返回 Failure
/// 期间返回 Running，轮数完成返回 Success
/// </summary>
public partial class UseBurstIfReadyThenDoActionsByCount : UseEQThenDoActions
{
    [BlackboardKey(Access = Access.Write)]
    public override BehaviourKeyAccess<Avatar> LeadCastingAvatar { get; set; } = null!;

    private UseBurstIfReadyThenDoActionsByCount(string name, Avatar avatar, int times, IEnumerable<Behaviour> children)
        : base(name, avatar, 0, times, children)
    {
    }

    protected override bool CastUseEQ()
    {
        // 未就绪直接 Failure，不切人、不产生无效按键
        if (!BehaviourHelper.CheckBurstReady(_avatar, out _))
        {
            return false;
        }

        // 先复位该角色的就绪缓存，后续 IsBurstReady 将重新检测
        _avatar.IsBurstReady = false;

        // TODO: Avatar.UseBurst 内部仍会自行检测就绪状态；待其支持信任外部就绪状态后，
        // 可信任 CheckBurstReady 的就绪结果跳过这次复检
        _avatar.Switch();
        _avatar.UseBurst();
        return true;
    }
}
