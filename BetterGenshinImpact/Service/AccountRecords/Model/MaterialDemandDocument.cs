using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>账号目录内独立的需求文件；随账号目录改名和删除。</summary>
public sealed class MaterialDemandDocument
{
    /// <summary>需求文件格式版本。</summary>
    [JsonProperty(Required = Required.Always)]
    public int SchemaVersion { get; set; } = 1;
    /// <summary>以库存标准名称关联的需求。</summary>
    [JsonProperty(Required = Required.Always)]
    public Dictionary<string, MaterialDemand> Items { get; set; } = [];
    /// <summary>已完成并由用户清理的需求历史，与活动需求原子保存。</summary>
    [JsonProperty(Required = Required.Always)]
    public List<MaterialDemandArchive> History { get; set; } = [];
    /// <summary>需求分页独立的玩家置顶物品，不与库存置顶同步。</summary>
    [JsonProperty(Required = Required.Always)]
    public HashSet<string> Prior { get; set; } = [];
}
