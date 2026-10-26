namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 通用原因码。原因码一旦发布就固定不变；只属于某个任务的原因码定义在该任务类旁边。
/// </summary>
public static class SoloTaskReasons
{
    /// <summary>开始时允许的树脂不足一次领奖，未执行挑战。</summary>
    public const string ResinExhausted = "resin_exhausted";

    /// <summary>
    /// 参数不满足业务要求。
    /// </summary>
    public const string InvalidParameter = "invalid_parameter";

    /// <summary>
    /// 战斗策略不存在。
    /// </summary>
    public const string StrategyNotFound = "strategy_not_found";

    /// <summary>
    /// 没有进入目标界面。
    /// </summary>
    public const string PageNotReached = "page_not_reached";

    /// <summary>
    /// 游戏运行环境已停止。
    /// </summary>
    public const string RuntimeStopped = "runtime_stopped";

    /// <summary>
    /// 达到次数或时间上限后结束。
    /// </summary>
    public const string LimitReached = "limit_reached";
}
