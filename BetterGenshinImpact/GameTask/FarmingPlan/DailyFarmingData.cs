using System;
using System.Collections.Generic;
using System.Linq;
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
    
    
    /// <summary>
    /// 当日米游社小怪上限统计
    /// </summary>
    [JsonProperty("miyoushe_total_normal_mob_count")]
    public double MiyousheTotalNormalMobCount { get; set; } = 0;
    
    /// <summary>
    /// 当日米游社精英上限统计
    /// </summary>
    [JsonProperty("miyoushe_total_elite_mob_count")]
    public double MiyousheTotalEliteMobCount { get; set; } = 0;
    
    /// <summary>
    /// 米游社数据最后更新时间（尝试更新时间）
    /// </summary>
    [JsonProperty("last_miyoushe_update_time")]
    public DateTime LastMiyousheUpdateTime  { get; set; } = DateTime.MinValue;
    
    /// <summary>
    /// 札记数据最后更新时间（札记数据种记录的最后一条记录数据）
    /// </summary>
    [JsonProperty("travels_diary_detail_manager_update_time")]
    public DateTime TravelsDiaryDetailManagerUpdateTime  { get; set; } = DateTime.MinValue;

    public bool EnableMiyousheStats()
    {
        return MiyousheTotalEliteMobCount + MiyousheTotalNormalMobCount > 0;
    }

    public (double TotalEliteMobCount, double TotalNormalMobCount) getFinalTotalMobCount()
    {
        if (MiyousheTotalEliteMobCount + MiyousheTotalNormalMobCount > 0)
        {
            //累计米游社数据
            //拿出超过札记统计的时间
            List<FarmingRecord> tdrs = Records.Where(r => r.Timestamp > TravelsDiaryDetailManagerUpdateTime).ToList();
            double sumEliteMobCount = tdrs.Sum(t => t.EliteMobCount);
            double sumNormalMobCount = tdrs.Sum(t => t.NormalMobCount);
            var totalEliteMobCount = MiyousheTotalEliteMobCount+sumEliteMobCount;
            var totalNormalMobCount = MiyousheTotalNormalMobCount+sumNormalMobCount;
            FarmingStatsRecorder.debugInfo($"精英统计：{totalEliteMobCount} = {MiyousheTotalEliteMobCount}+{sumEliteMobCount}");
            FarmingStatsRecorder.debugInfo($"小怪统计：{totalNormalMobCount} = {MiyousheTotalNormalMobCount}+{sumNormalMobCount}");
            return  (totalEliteMobCount,totalNormalMobCount);
        }
        return (TotalEliteMobCount, TotalNormalMobCount);
    }

    public (double DailyEliteCap, double DailyMobCap) getFinalCap()
    {
        var config = TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig;
        var mysdCfg = config.MiyousheDataConfig;
        if (MiyousheTotalEliteMobCount + MiyousheTotalNormalMobCount > 0)
        {
            //上限切换成米游社配置里面的
            return (mysdCfg.DailyEliteCap,mysdCfg.DailyMobCap);
        }
        return (config.DailyEliteCap,config.DailyMobCap);
    }
}

