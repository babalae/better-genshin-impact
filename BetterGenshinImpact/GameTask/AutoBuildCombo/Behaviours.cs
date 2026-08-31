using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.Helpers;
using CsTrees;
using CsTrees.Blackboard;
using System;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 队伍数据访问辅助
/// </summary>
internal static class BehaviourHelper
{
    /// <summary>
    /// 按名字解析目标角色
    /// </summary>
    public static Avatar? ResolveAvatar(CombatScenes combatScenes, string avatarName)
    {
        return combatScenes.SelectAvatar(avatarName);
    }

    /// <summary>
    /// 切换到目标角色（合并切人语义）
    /// Switch 内部会先截图判断是否已在该角色，再决定是否按键重试，已在场则无操作
    /// </summary>
    public static Avatar? SwitchIfNeeded(CombatScenes combatScenes, string avatarName)
    {
        var avatar = ResolveAvatar(combatScenes, avatarName);
        if (avatar == null)
        {
            return null;
        }

        avatar.Switch();
        return avatar;
    }

    /// <summary>
    /// 检测指定角色Q爆发是否就绪（IsBurstReady 与 UseBurstIfReady 共用判定）
    /// cached 输出本次结果是否来自缓存
    /// </summary>
    public static bool CheckBurstReady(Avatar avatar, BehaviourKeyAccess<BurstReadyState>? port, out bool cached)
    {
        using var frame = CaptureToRectArea();

        // 场上角色不读缓存，每次都重新检测，避免切人后命中过期状态；检测结果无论在场与否都写入缓存
        // （就绪状态在释放前单调，在场检测到就绪后切回后台可直接命中）
        // 在场路径用与 UseBurst 内部同源的 ONNX 分类器判定（连通域检测会把"能量未充满"误判为就绪）；
        // 场下路径用侧边栏 Hough 圆环检测。分类器低置信度返回 Unknown，按未就绪处理
        var isActive = Bv.IsCharacterActive(frame, avatar.Index);
        cached = !isActive && port != null && port.TryGet(out var state) && state == BurstReadyState.Ready;

        var ready = cached || (isActive
            ? Avatar.IsBurstReadyByClassify(frame) == BurstReadyState.Ready
            : Bv.IsSkillReady(frame, avatar.Index, true));
        if (ready && !cached)
        {
            port?.Set(BurstReadyState.Ready);
        }

        return ready;
    }
}

/// <summary>
/// 使用元素战技（E）
/// 若目标角色不在场，先切换到该角色再释放；释放后自动 OCR 冷却并记录
/// </summary>
public partial class UseSkill : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly bool _hold;

    private UseSkill(string name, string avatarName, bool hold) : base(name)
    {
        _avatarName = avatarName;
        _hold = hold;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        avatar.UseSkill(_hold);
        return Status.Success;
    }
}

/// <summary>
/// 使用元素爆发（Q）
/// 若目标角色不在场，先切换到该角色再释放；Q 未就绪时内部直接跳过（不视为失败）
/// </summary>
public partial class UseBurst : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady1 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady2 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady3 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady4 { get; private set; } = null!;

    private readonly string _avatarName;

    private UseBurst(string name, string avatarName) : base(name)
    {
        _avatarName = avatarName;
    }

    /// <summary>按队伍序号取对应黑板端口</summary>
    private BehaviourKeyAccess<BurstReadyState>? Port(int index)
    {
        return index switch
        {
            1 => BurstReady1,
            2 => BurstReady2,
            3 => BurstReady3,
            4 => BurstReady4,
            _ => null,
        };
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        var port = Port(avatar.Index);

        // 先复位该角色的缓存条目（Unset 删键），后续 IsBurstReady 将重新检测
        port?.Unset();

        // TODO: Avatar.UseBurst 内部仍会自行检测就绪状态；待其支持信任外部就绪状态后，
        // 可信任 IsBurstReady 缓存的 Ready 跳过这次复检
        avatar.UseBurst();

        return Status.Success;
    }
}

/// <summary>
/// 查询E技能是否就绪（条件节点）
/// 基于 ESkillCdTracker 冷却记录，就绪返回 Success，冷却中返回 Failure。
/// 不切人、不阻塞、无副作用，供 Selector/Sequence 组合表达"就绪才放、没好兜底"的策略。
/// </summary>
public partial class IsSkillReady : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;

    private IsSkillReady(string name, string avatarName) : base(name)
    {
        _avatarName = avatarName;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.ResolveAvatar(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        return ESkillCdTracker.IsReady(avatar.Name) ? Status.Success : Status.Failure;
    }
}

/// <summary>
/// 查询Q爆发是否就绪（条件节点）
/// 场上角色用 ONNX 分类器检测底部中央图标（与 UseBurst 内部判定同源），场下角色检测右侧队伍栏图标。
/// 仅缓存就绪结果（黑板键：BurstReady1 ~ BurstReady4，按队伍序号分键）——就绪状态在释放前是单调的，
/// 直至 UseBurst 释放后 Unset 复位；未就绪不缓存，每次都重新检测。
/// 场上角色不读缓存（避免切人后命中过期状态），每次都重新检测，但检测结果仍写入缓存；
/// 场下角色命中就绪缓存时直接返回，未就绪不缓存，每次都重新检测。
/// 就绪返回 Success，未就绪返回 Failure。不切人、不阻塞、无按键副作用。
/// </summary>
public partial class IsBurstReady : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady1 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady2 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady3 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady4 { get; private set; } = null!;

    private readonly string _avatarName;

    private IsBurstReady(string name, string avatarName) : base(name)
    {
        _avatarName = avatarName;
    }

    /// <summary>按队伍序号取对应黑板端口</summary>
    private BehaviourKeyAccess<BurstReadyState>? Port(int index)
    {
        return index switch
        {
            1 => BurstReady1,
            2 => BurstReady2,
            3 => BurstReady3,
            4 => BurstReady4,
            _ => null,
        };
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.ResolveAvatar(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        return BehaviourHelper.CheckBurstReady(avatar, Port(avatar.Index), out _)
            ? Status.Success
            : Status.Failure;
    }
}

/// <summary>
/// 检查Q爆发就绪后释放（条件+动作合一节点）
/// 未就绪返回 Failure，不切人、不按键、无副作用；就绪则切换到目标角色并释放，释放后返回 Success。
/// 相比 IsBurstReady+UseBurst 组合，未就绪时不会静默成功，可避免高优先级分支假成功堵死 Selector
/// </summary>
public partial class UseBurstIfReady : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady1 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady2 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady3 { get; private set; } = null!;

    [BlackboardKey(Access = Access.Write)]
    public BehaviourKeyAccess<BurstReadyState> BurstReady4 { get; private set; } = null!;

    private readonly string _avatarName;

    private UseBurstIfReady(string name, string avatarName) : base(name)
    {
        _avatarName = avatarName;
    }

    /// <summary>按队伍序号取对应黑板端口</summary>
    private BehaviourKeyAccess<BurstReadyState>? Port(int index)
    {
        return index switch
        {
            1 => BurstReady1,
            2 => BurstReady2,
            3 => BurstReady3,
            4 => BurstReady4,
            _ => null,
        };
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.ResolveAvatar(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        var port = Port(avatar.Index);

        // 未就绪直接 Failure，上层 Selector 自然落到下位替代；不切人、不产生无效按键
        if (!BehaviourHelper.CheckBurstReady(avatar, port, out _))
        {
            return Status.Failure;
        }

        if (BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName) == null)
        {
            return Status.Failure;
        }

        // 先复位该角色的缓存条目（Unset 删键），后续 IsBurstReady 将重新检测
        port?.Unset();

        // TODO: Avatar.UseBurst 内部仍会自行检测就绪状态；待其支持信任外部就绪状态后，
        // 可信任 CheckBurstReady 的就绪结果跳过这次复检
        avatar.UseBurst();

        return Status.Success;
    }
}

/// <summary>
/// 检查E战技就绪后释放（条件+动作合一节点）
/// 基于 ESkillCdTracker 冷却记录判断：未就绪返回 Failure，不切人、不按键；就绪则切换到目标角色并释放，返回 Success。
/// 相比 IsSkillReady+UseSkill 组合，未就绪时不会无效按键，也不会假成功堵死 Selector
/// </summary>
public partial class UseSkillIfReady : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly bool _hold;

    private UseSkillIfReady(string name, string avatarName, bool hold) : base(name)
    {
        _avatarName = avatarName;
        _hold = hold;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.ResolveAvatar(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        // 未就绪直接 Failure，上层 Selector 自然落到下位替代；不切人、不产生无效按键
        if (!ESkillCdTracker.IsReady(avatar.Name))
        {
            return Status.Failure;
        }

        if (BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName) == null)
        {
            return Status.Failure;
        }

        avatar.UseSkill(_hold);
        return Status.Success;
    }
}

/// <summary>
/// 普通攻击，每 0.2 秒点击一次左键，持续指定时长
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Attack : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly double _seconds;

    private Attack(string name, string avatarName, double seconds) : base(name)
    {
        AssertUtils.IsTrue(seconds > 0, "attack时长必须大于0");
        _avatarName = avatarName;
        _seconds = seconds;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        avatar.Attack((int)TimeSpan.FromSeconds(_seconds).TotalMilliseconds);
        return Status.Success;
    }
}

/// <summary>
/// 重击（长按左键），持续指定时长
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Charge : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly double _seconds;

    private Charge(string name, string avatarName, double seconds) : base(name)
    {
        AssertUtils.IsTrue(seconds > 0, "charge时长必须大于0");
        _avatarName = avatarName;
        _seconds = seconds;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        avatar.Charge((int)TimeSpan.FromSeconds(_seconds).TotalMilliseconds);
        return Status.Success;
    }
}

/// <summary>
/// 行走指定方向，持续指定时长
/// direction 为 w/a/s/d（不区分大小写）；若目标角色不在场，先切换到该角色
/// </summary>
public partial class Walk : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly string _direction;
    private readonly double _seconds;

    private Walk(string name, string avatarName, string direction, double seconds) : base(name)
    {
        direction = direction.Trim().ToLowerInvariant();
        AssertUtils.IsTrue(direction is "w" or "a" or "s" or "d", $"walk方向必须是w/a/s/d，当前是{direction}");
        AssertUtils.IsTrue(seconds > 0, "walk时长必须大于0");
        _avatarName = avatarName;
        _direction = direction;
        _seconds = seconds;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        avatar.Walk(_direction, (int)TimeSpan.FromSeconds(_seconds).TotalMilliseconds);
        return Status.Success;
    }
}

/// <summary>
/// 冲刺，持续指定时长
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Dash : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;
    private readonly double _seconds;

    private Dash(string name, string avatarName, double seconds) : base(name)
    {
        AssertUtils.IsTrue(seconds > 0, "dash时长必须大于0");
        _avatarName = avatarName;
        _seconds = seconds;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        avatar.Dash((int)TimeSpan.FromSeconds(_seconds).TotalMilliseconds);
        return Status.Success;
    }
}

/// <summary>
/// 跳跃
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Jump : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    private readonly string _avatarName;

    private Jump(string name, string avatarName) : base(name)
    {
        _avatarName = avatarName;
    }

    protected async override Task<Status> Update()
    {
        var avatar = BehaviourHelper.SwitchIfNeeded(CombatScenes.Get(), _avatarName);
        if (avatar == null)
        {
            return Status.Failure;
        }

        avatar.Jump();
        return Status.Success;
    }
}

