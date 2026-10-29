using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 活动页识别到的移涌活动。每天合计 3 次双倍奖励，只对原粹树脂生效。
/// </summary>
public enum ResinPlanOverflowEvent
{
    /// <summary>地脉移涌：地脉之花的摩拉与角色经验书双倍。</summary>
    LeyLineOverflow,

    /// <summary>盛材移涌（旧名精通移涌）：天赋与武器突破素材双倍。</summary>
    MaterialOverflow,

    /// <summary>祝圣移涌：圣遗物双倍。</summary>
    BlessingOverflow
}

/// <summary>
/// 幽境危战紊乱爆发期的识别结果。
/// </summary>
public enum StygianBurstState
{
    /// <summary>进入了危战页面，但没有读到紊乱爆发期的提示。</summary>
    Unknown,

    /// <summary>活动列表中没有找到幽境危战，或未能切换到危战页面。</summary>
    NotFound,

    /// <summary>危战页面显示紊乱爆发期，且没有“已结束”。</summary>
    Active,

    /// <summary>危战页面显示“紊乱爆发期已结束”。</summary>
    Ended
}

/// <summary>
/// 体力计划准备阶段的活动识别结果，随体力计划结果保存。
/// 只支持简体中文界面；识别失败按“没有活动”处理。
/// </summary>
public sealed class ResinPlanEventReport
{
    /// <summary>是否成功打开活动页并读取了活动列表；为 false 时其余字段不代表游戏状态。</summary>
    public bool Recognized { get; set; }

    /// <summary>活动列表中出现的移涌活动及各自今日剩余的双倍次数。</summary>
    public List<ResinPlanOverflowStatus> Overflows { get; set; } = [];

    /// <summary>幽境危战紊乱爆发期的状态。</summary>
    public StygianBurstState StygianBurst { get; set; } = StygianBurstState.NotFound;

    /// <summary>识别失败时的说明；成功时为 null。</summary>
    public string? Message { get; set; }
}

/// <summary>
/// 一个移涌活动的识别结果：活动种类与活动详情中的“今日剩余多倍产出次数”。
/// </summary>
public sealed class ResinPlanOverflowStatus
{
    /// <summary>移涌活动种类。</summary>
    public ResinPlanOverflowEvent Event { get; set; }

    /// <summary>今日剩余的双倍次数；“已用完”为 0，未能读取时为 null（调度时按完整 3 次安排）。</summary>
    public int? RemainingClaims { get; set; }

    /// <summary>每日双倍次数上限，读取到“剩余/上限”时记录；未读取时为 null。</summary>
    public int? DailyClaims { get; set; }
}
