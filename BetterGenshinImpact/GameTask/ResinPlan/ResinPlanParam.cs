using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划参数：本次可用的树脂、是否先合成浓缩，以及四个消耗树脂任务的开关与各自设置。
/// 账号由一条龙负责，培养目标以游戏内养成计划为准，这里不保存跨运行的进度。
/// </summary>
public class ResinPlanParam
{
    /// <summary>本次最多消耗的树脂点数；0 表示用完当前全部原粹和浓缩树脂。浓缩每个计 60 点。</summary>
    public int MaxResin { get; set; }

    /// <summary>刷取前是否先把可用的原粹树脂合成浓缩树脂。</summary>
    public bool CraftCondensedFirst { get; set; }

    /// <summary>
    /// 是否优先满足活动：识别到移涌活动时先在活动适用的内容里用原粹领取当天的双倍；
    /// 紊乱爆发期进行中时幽境危战优先使用树脂，并暂停首领与秘境养成。默认开启。
    /// </summary>
    public bool EventPriority { get; set; } = true;

    /// <summary>
    /// 移涌领取是否严格使用 20 档：开启时剩余 3 次双倍用 20 档领取 3 次（60 原粹）；
    /// 关闭时用 40 档领取 2 次（80 原粹），少打一次。
    /// </summary>
    public bool EventStrictResin { get; set; }

    /// <summary>自动秘境：根据提升指南选择秘境养成，设置中的备选秘境用于消耗剩余树脂。</summary>
    public ResinPlanSubTask<AutoDomainParam> Domain { get; set; } = new();

    /// <summary>
    /// 自动首领讨伐；战斗不稳定，默认不开启。讨伐次数沿用首领自己的树脂使用设置（指定次数或刷至耗尽），
    /// 在体力计划中默认只讨伐 1 次，避免开启后在养成之前用光树脂。
    /// </summary>
    public ResinPlanSubTask<AutoBossParam> Boss { get; set; } = new(CreateDefaultBoss) { Enabled = false };

    /// <summary>自动幽境危战；只在紊乱爆发期能够消耗树脂。</summary>
    public ResinPlanSubTask<AutoStygianOnslaughtParam> Stygian { get; set; } = new();

    /// <summary>自动地脉花。</summary>
    public ResinPlanSubTask<AutoLeyLineOutcropParam> LeyLine { get; set; } = new();

    /// <summary>合成浓缩树脂的设置；合成份数由体力计划计算。</summary>
    public CraftCondensedResinParam Craft { get; set; } = new();

    /// <summary>体力计划内首领的默认参数：其余设置沿用首领配置，树脂改为只用原粹讨伐 1 次。</summary>
    /// <returns>首领参数。</returns>
    private static AutoBossParam CreateDefaultBoss() => new()
    {
        ResinUsage = new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 1, Allowed = new() { Original = true } }
    };
}
