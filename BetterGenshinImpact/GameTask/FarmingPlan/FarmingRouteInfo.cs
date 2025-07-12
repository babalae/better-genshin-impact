using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.FarmingPlan;

/// <summary>
/// 表示锄地路径信息
/// </summary>
public class FarmingRouteInfo
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
    /// 路径中的普通怪物数量
    /// </summary>
    [JsonProperty("normal_mob_count")]
    public double NormalMobCount { get; set; } = 0;

    /// <summary>
    /// 路径中的精英怪物数量
    /// </summary>
    [JsonProperty("elite_mob_count")]
    public double EliteMobCount { get; set; } = 0;
}
