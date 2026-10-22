namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取每日奖励任务的参数。
/// </summary>
public class DailyRewardsParam
{
    /// <summary>
    /// 前往哪个国家的冒险家协会，对应路线文件“冒险家协会_{国家}.json”。
    /// </summary>
    public string Country { get; set; } = "蒙德";

    /// <summary>
    /// 领取每日委托奖励前要切换到的队伍名称（通常是好感队）；为空时不切换队伍。
    /// </summary>
    public string? PartyName { get; set; }
}
