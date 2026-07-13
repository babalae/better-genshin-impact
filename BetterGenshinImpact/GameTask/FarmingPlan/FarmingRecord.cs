using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.FarmingPlan;

/// <summary>
/// 表示单次锄地记录
/// </summary>
public class FarmingRecord
{
    /// <summary>
    /// 路径组名称
    /// </summary>
    [JsonProperty("group_name")]
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// 项目名称
    /// </summary>
    [JsonProperty("project_name")]
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// 文件夹名称
    /// </summary>
    [JsonProperty("folder_name")]
    public string FolderName { get; set; } = string.Empty;

    /// <summary>
    /// 本次普通怪物数量
    /// </summary>
    [JsonProperty("normal_mob_count")]
    public double NormalMobCount { get; set; } = 0;

    /// <summary>
    /// 本次精英怪物数量
    /// </summary>
    [JsonProperty("elite_mob_count")]
    public double EliteMobCount { get; set; } = 0;

    /// <summary>
    /// 记录时间戳（ISO 8601格式）
    /// </summary>
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
