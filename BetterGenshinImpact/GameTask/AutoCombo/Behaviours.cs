using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.Helpers;
using CsTrees;
using CsTrees.Blackboard;
using System;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoCombo;

/// <summary>
/// 爆发就绪检测辅助
/// </summary>
internal static class BehaviourHelper
{
    /// <summary>
    /// 检测指定角色Q爆发是否就绪（IsBurstReady 与 UseBurstIfReady 共用判定）
    /// cached 输出本次结果是否来自缓存
    /// </summary>
    public static bool CheckBurstReady(Avatar avatar, out bool cached)
    {
        using var frame = CaptureToRectArea();

        // 场上角色不读缓存，每次都重新检测，避免切人后命中过期状态；检测结果无论在场与否都写入缓存
        // （就绪状态在释放前单调，在场检测到就绪后切回后台可直接命中）
        // 在场路径用与 UseBurst 内部同源的 ONNX 分类器判定（连通域检测会把"能量未充满"误判为就绪）；
        // 场下路径用侧边栏 Hough 圆环检测。分类器低置信度返回 Unknown，按未就绪处理
        var isActive = Bv.IsCharacterActive(frame, avatar.Index);
        cached = !isActive && avatar.IsBurstReady;

        var ready = cached || (isActive
            ? Avatar.IsBurstReadyByClassify(frame) == BurstReadyState.Ready
            : Bv.IsSkillReady(frame, avatar.Index, true));
        if (ready && !cached)
        {
            avatar.IsBurstReady = true;
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
    private readonly Avatar _avatar;
    private readonly bool _hold;

    public UseSkill(string name, Avatar avatar, bool hold) : base(name)
    {
        _avatar = avatar;
        _hold = hold;
    }

    protected async override Task<Status> Update()
    {
        // Switch 内部会先截图判断是否已在该角色，再决定是否按键重试，已在场则无操作
        _avatar.Switch();
        _avatar.UseSkill(_hold);
        return Status.Success;
    }
}

/// <summary>
/// 使用元素爆发（Q）
/// 若目标角色不在场，先切换到该角色再释放；Q 未就绪时内部直接跳过（不视为失败）
/// </summary>
public partial class UseBurst : Behaviour
{
    public UseBurst(string name, Avatar avatar) : base(name)
    {
        _avatar = avatar;
    }

    private readonly Avatar _avatar;

    protected async override Task<Status> Update()
    {
        // 先复位该角色的就绪缓存，后续 IsBurstReady 将重新检测
        _avatar.IsBurstReady = false;

        // TODO: Avatar.UseBurst 内部仍会自行检测就绪状态；待其支持信任外部就绪状态后，
        // 可信任 IsBurstReady 缓存的 Ready 跳过这次复检
        _avatar.Switch();
        _avatar.UseBurst();

        return Status.Success;
    }
}

/// <summary>
/// 查询E技能是否就绪（条件节点）
/// 基于 Avatar 三态冷却记录判断：就绪返回 Success，冷却中或未知返回 Failure。
/// 冷却状态未知（用过 E 但没读到 CD）时，切人刷新一次 OCR 识别消歧后再判定。
/// </summary>
public partial class IsSkillReady : Behaviour
{
    private readonly Avatar _avatar;

    private IsSkillReady(string name, Avatar avatar) : base(name)
    {
        _avatar = avatar;
    }

    /// <summary>
    /// 黑板键（只读）：UseEQThenDoActions 起手技流程中写入自身 _avatar。
    /// 读到与自身 _avatar 匹配 → 处于某 UseEQ 子节点链 → 判 02/03 就绪；
    /// 读不到或不匹配 → 默认判原始 E 01。
    /// </summary>
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<Avatar> LeadCastingAvatar { get; private set; } = null!;

    protected async override Task<Status> Update()
    {
        // 读到匹配角色 → 处于某 UseEQ 子节点链 → 放宽E技能图标判定；否则只认01
        var inLeadCast = LeadCastingAvatar.TryGet(out var lead) && ReferenceEquals(lead, _avatar);
        var state = _avatar.GetSkillCdState(!inLeadCast);
        if (state == SkillCdState.Unknown)
        {
            // E 冷却图标只显示当前场上角色，切人后让视觉判定生效（GetSkillCdState 内部已含 OCR 兜底）
            _avatar.Switch();
            state = _avatar.GetSkillCdState(!inLeadCast);
        }

        return state == SkillCdState.Ready ? Status.Success : Status.Failure;
    }
}

/// <summary>
/// 查询Q爆发是否就绪（条件节点）
/// 场上角色用 ONNX 分类器检测底部中央图标（与 UseBurst 内部判定同源），场下角色检测右侧队伍栏图标。
/// 仅缓存就绪结果（Avatar.IsBurstReady）——就绪状态在释放前是单调的，
/// 直至 UseBurst 释放后复位；未就绪不缓存，每次都重新检测。
/// 场上角色不读缓存（避免切人后命中过期状态），每次都重新检测，但检测结果仍写入缓存；
/// 场下角色命中就绪缓存时直接返回，未就绪不缓存，每次都重新检测。
/// 就绪返回 Success，未就绪返回 Failure。不切人、不阻塞、无按键副作用。
/// </summary>
public partial class IsBurstReady : Behaviour
{
    public IsBurstReady(string name, Avatar avatar) : base(name)
    {
        _avatar = avatar;
    }

    private readonly Avatar _avatar;

    protected async override Task<Status> Update()
    {
        return BehaviourHelper.CheckBurstReady(_avatar, out _)
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
    public UseBurstIfReady(string name, Avatar avatar) : base(name)
    {
        _avatar = avatar;
    }

    private readonly Avatar _avatar;

    protected async override Task<Status> Update()
    {
        // 未就绪直接 Failure，上层 Selector 自然落到下位替代；不切人、不产生无效按键
        if (!BehaviourHelper.CheckBurstReady(_avatar, out _))
        {
            return Status.Failure;
        }

        // 先复位该角色的就绪缓存，后续 IsBurstReady 将重新检测
        _avatar.IsBurstReady = false;

        // TODO: Avatar.UseBurst 内部仍会自行检测就绪状态；待其支持信任外部就绪状态后，
        // 可信任 CheckBurstReady 的就绪结果跳过这次复检
        _avatar.Switch();
        _avatar.UseBurst();

        return Status.Success;
    }
}

/// <summary>
/// 检查E战技就绪后释放（条件+动作合一节点）
/// 基于 Avatar 冷却记录判断：冷却中返回 Failure，不切人、不按键；就绪则切换到目标角色并释放，返回 Success。
/// 冷却状态未知（用过 E 但没读到 CD）时，切人刷新一次 OCR 识别消歧：仍就绪才释放，否则返回 Failure。
/// 相比 IsSkillReady+UseSkill 组合，未就绪时不会无效按键，也不会假成功堵死 Selector
/// </summary>
public partial class UseSkillIfReady : Behaviour
{
    private readonly Avatar _avatar;
    private readonly bool _hold;

    private UseSkillIfReady(string name, Avatar avatar, bool hold) : base(name)
    {
        _avatar = avatar;
        _hold = hold;
    }

    /// <summary>
    /// 黑板键（只读）：UseEQThenDoActions 起手技流程中写入自身 _avatar。
    /// 读到与自身 _avatar 匹配 → 处于某 UseEQ 子节点链 → 判 02/03 就绪；
    /// 读不到或不匹配 → 默认判原始 E 01。
    /// </summary>
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<Avatar> LeadCastingAvatar { get; private set; } = null!;

    protected async override Task<Status> Update()
    {
        // 读到匹配角色 → 处于某 UseEQ 子节点链 → 放宽E技能图标判定；否则只认01
        var inLeadCast = LeadCastingAvatar.TryGet(out var lead) && ReferenceEquals(lead, _avatar);
        var state = _avatar.GetSkillCdState(!inLeadCast);
        if (state == SkillCdState.Unknown)
        {
            // E 冷却图标只显示当前场上角色，切人后让视觉判定生效（GetSkillCdState 内部已含 OCR 兜底）
            _avatar.Switch();
            state = _avatar.GetSkillCdState(!inLeadCast);
        }

        // 只有 Ready 才释放，Unknown/Cooldown 都视为未就绪
        if (state != SkillCdState.Ready)
        {
            return Status.Failure;
        }

        _avatar.Switch();
        _avatar.UseSkill(_hold);
        return Status.Success;
    }
}

/// <summary>
/// 普通攻击（非阻塞）：每 0.2 秒点击一次左键，持续指定时长，期间返回 Running，时间到返回 Success
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Attack : Behaviour
{
    private readonly Avatar _avatar;
    private readonly double _seconds;
    private readonly TimeProvider _timeProvider;

    /// <summary>点击间隔（毫秒），与 Avatar.Attack 的节奏一致</summary>
    private const int ClickIntervalMs = 200;

    /// <summary>窗口起点；null 表示尚未开始（首次 Tick 时切人并点击第一下）</summary>
    private DateTimeOffset? _startAt;

    private DateTimeOffset _lastClickAt;

    public Attack(string name, Avatar avatar, double seconds, TimeProvider? timeProvider = null) : base(name)
    {
        AssertUtils.IsTrue(seconds > 0, "attack时长必须大于0");
        _avatar = avatar;
        _seconds = seconds;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override void Initialize()
    {
        _startAt = null;
    }

    protected async override Task<Status> Update()
    {
        var now = _timeProvider.GetLocalNow();

        // 首次进入：切人，记录窗口起点
        if (_startAt == null)
        {
            _avatar.Switch();
            _startAt = now;
        }

        // 时间到：窗口正常结束（首拍 now - _startAt == 0，不可能命中）
        if (now - _startAt >= TimeSpan.FromSeconds(_seconds))
        {
            return Status.Success;
        }

        // 按固定节奏点击，两次点击不足间隔时本拍空转
        if (now - _lastClickAt >= TimeSpan.FromMilliseconds(ClickIntervalMs))
        {
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            _lastClickAt = now;
        }

        return Status.Running;
    }
}

/// <summary>
/// 重击（长按左键），持续指定时长
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Charge : Behaviour
{
    private readonly Avatar _avatar;
    private readonly double _seconds;

    public Charge(string name, Avatar avatar, double seconds) : base(name)
    {
        AssertUtils.IsTrue(seconds > 0, "charge时长必须大于0");
        _avatar = avatar;
        _seconds = seconds;
    }

    protected async override Task<Status> Update()
    {
        _avatar.Switch();
        _avatar.Charge((int)TimeSpan.FromSeconds(_seconds).TotalMilliseconds);
        return Status.Success;
    }
}

/// <summary>
/// 行走指定方向，持续指定时长
/// direction 为 w/a/s/d（不区分大小写）；若目标角色不在场，先切换到该角色
/// </summary>
public partial class Walk : Behaviour
{
    private readonly Avatar _avatar;
    private readonly string _direction;
    private readonly double _seconds;

    public Walk(string name, Avatar avatar, string direction, double seconds) : base(name)
    {
        direction = direction.Trim().ToLowerInvariant();
        AssertUtils.IsTrue(direction is "w" or "a" or "s" or "d", $"walk方向必须是w/a/s/d，当前是{direction}");
        AssertUtils.IsTrue(seconds > 0, "walk时长必须大于0");
        _avatar = avatar;
        _direction = direction;
        _seconds = seconds;
    }

    protected async override Task<Status> Update()
    {
        _avatar.Switch();
        _avatar.Walk(_direction, (int)TimeSpan.FromSeconds(_seconds).TotalMilliseconds);
        return Status.Success;
    }
}

/// <summary>
/// 冲刺，持续指定时长
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Dash : Behaviour
{
    private readonly Avatar _avatar;
    private readonly double _seconds;

    public Dash(string name, Avatar avatar, double seconds) : base(name)
    {
        AssertUtils.IsTrue(seconds > 0, "dash时长必须大于0");
        _avatar = avatar;
        _seconds = seconds;
    }

    protected async override Task<Status> Update()
    {
        _avatar.Switch();
        _avatar.Dash((int)TimeSpan.FromSeconds(_seconds).TotalMilliseconds);
        return Status.Success;
    }
}

/// <summary>
/// 跳跃
/// 若目标角色不在场，先切换到该角色
/// </summary>
public partial class Jump : Behaviour
{
    private readonly Avatar _avatar;

    public Jump(string name, Avatar avatar) : base(name)
    {
        _avatar = avatar;
    }

    protected async override Task<Status> Update()
    {
        _avatar.Switch();
        _avatar.Jump();
        return Status.Success;
    }
}
