namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划专用的原因码；通用原因（如树脂不足、参数无效）使用 SoloTaskReasons。
/// </summary>
public static class ResinPlanReasons
{
    /// <summary>未能读取当前树脂数量，没有消耗树脂。</summary>
    public const string ResinUnknown = "resin_unknown";

    /// <summary>已达到本次体力用量上限。</summary>
    public const string BudgetReached = "budget_reached";

    /// <summary>可用原粹不足一份或浓缩已达持有上限，无需合成。</summary>
    public const string CraftNotNeeded = "craft_not_needed";

    /// <summary>剩余树脂没有可承接的任务：未设置备选秘境。</summary>
    public const string NoFallbackDomain = "no_fallback_domain";

    /// <summary>子任务抛出了未处理的异常（例如提升指南识别失败）。</summary>
    public const string TaskException = "task_exception";

    /// <summary>有树脂但本次没有任何领奖，例如养成计划没有需求且其余任务都未开启。</summary>
    public const string NothingClaimed = "nothing_claimed";

    /// <summary>移涌活动今日双倍次数已用完，不再单独领取。</summary>
    public const string EventClaimsUsedUp = "event_claims_used_up";

    /// <summary>活动对应的子任务未开启，例如地脉移涌需要开启自动地脉花。</summary>
    public const string EventTaskDisabled = "event_task_disabled";

    /// <summary>祝圣移涌没有可用的祝圣秘境：备选秘境为空或不是祝圣秘境。</summary>
    public const string EventDomainMissing = "event_domain_missing";

    /// <summary>紊乱爆发期优先使用树脂，本阶段不执行。</summary>
    public const string StygianBurst = "stygian_burst";
}
