namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// E 元素战技冷却状态
/// </summary>
public enum SkillCdState
{
    /// <summary>就绪：从未使用过，或冷却记录已到期 / 保守估计窗口已过</summary>
    Ready,
    /// <summary>冷却中：最近一次使用后 OCR 到有效 CD 且未到期，或手动配置 CD 未走完</summary>
    Cooldown,
    /// <summary>未知：OCR 视角下表示冷却记录已过期（用过但没读到 CD）；综合视角下表示处于按完整 CD 保守估计的窗口内</summary>
    Unknown
}
