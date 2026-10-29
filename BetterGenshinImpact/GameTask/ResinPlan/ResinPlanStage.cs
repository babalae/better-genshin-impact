namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划内部按固定顺序执行的阶段。
/// </summary>
public enum ResinPlanStage
{
    /// <summary>合成浓缩树脂。</summary>
    Craft,

    /// <summary>首领讨伐。</summary>
    Boss,

    /// <summary>根据提升指南养成。</summary>
    TrainingDomain,

    /// <summary>剩余树脂：幽境危战。</summary>
    Stygian,

    /// <summary>剩余树脂：地脉花。</summary>
    LeyLine,

    /// <summary>剩余树脂：备选秘境。</summary>
    FallbackDomain,

    /// <summary>活动优先：移涌领取与紊乱爆发期的幽境危战。追加在末尾，旧记录按枚举名读取不受影响。</summary>
    Event
}
