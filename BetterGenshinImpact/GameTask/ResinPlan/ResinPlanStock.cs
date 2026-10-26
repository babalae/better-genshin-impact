namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 一次读取到的树脂持有量。
/// </summary>
/// <param name="Original">原粹树脂当前值。</param>
/// <param name="Max">原粹树脂自然恢复上限。</param>
/// <param name="Condensed">浓缩树脂数量；顶栏显示了浓缩图标但数量识别失败时为 null，此时至少有 1 个。</param>
public sealed record ResinPlanStock(int Original, int Max, int? Condensed)
{
    /// <summary>能否领取一次秘境、幽境或地脉花奖励：原粹至少 20，或持有浓缩树脂。</summary>
    public bool CanClaim => Original >= ResinPlanBudget.MinClaimPoints || Condensed != 0;

    /// <summary>用于计算的浓缩数量；数量未知时按 0 计，不规划浓缩的使用次数。</summary>
    public int KnownCondensed => Condensed ?? 0;
}
