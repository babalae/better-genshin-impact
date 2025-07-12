using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.FarmingPlan;

/// <summary>
/// 表示一次锄地会话的统计数据
/// </summary>
public class FarmingSession
{
    /// <summary>
    /// 是否允许战斗统计
    /// </summary>
    [JsonProperty("allow_farming_count")]
    public bool AllowFarmingCount { get; set; } = false;

    /// <summary>
    /// 普通怪物数量
    /// </summary>
    [JsonProperty("normal_mob_count")]
    public double NormalMobCount { get; set; } = 0;

    /// <summary>
    /// 精英怪物数量
    /// </summary>
    [JsonProperty("elite_mob_count")]
    public double EliteMobCount { get; set; } = 0;

    /// <summary>
    /// 主目标，值为elite、normal时，所配置的类别达到上限时，就会跳过该路径，
    /// 如果填写disable代表非锄地脚本（如挖矿战斗，也会纳入统计，即使达到上限，但不影响继续执行），
    /// 如果不填，或其他值，则两种都达到上限（当然另一种目标个数为0，也会跳过）才会跳过。
    /// </summary>
    [JsonProperty("primary_target")]
    public string PrimaryTarget { get; set; } = "";

    /// <summary>
    /// 本次锄地耗时（秒）
    /// </summary>
    [JsonProperty("duration_seconds")]
    public double DurationSeconds { get; set; } = 0;

    /// <summary>
    /// 精英详细
    /// </summary>
    [JsonProperty("elite_details")]
    public string EliteDetails { get; set; } = "";
    
    /// <summary>
    /// 本次锄地获得的总摩拉
    /// </summary>
    [JsonProperty("total_mora")]
    public double TotalMora { get; set; } = 0;
}