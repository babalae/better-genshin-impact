using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.FarmingPlan;


/// <summary>
/// 表示每日锄地统计数据
/// </summary>
public class DailyFarmingData
{
    /// <summary>
    /// 当日累计普通怪物总数
    /// </summary>
    [JsonProperty("total_normal_mob_count")]
    public double TotalNormalMobCount { get; set; } = 0;

    /// <summary>
    /// 当日累计精英怪物总数
    /// </summary>
    [JsonProperty("total_elite_mob_count")]
    public double TotalEliteMobCount { get; set; } = 0;

    /// <summary>
    /// 当日所有锄地记录
    /// </summary>
    [JsonProperty("records")]
    public List<FarmingRecord> Records { get; set; } = new List<FarmingRecord>();
    
    [JsonIgnore]
    public string FilePath { get; set; } ="";
}

