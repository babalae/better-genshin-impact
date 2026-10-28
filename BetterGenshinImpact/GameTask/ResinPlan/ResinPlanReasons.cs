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
}
