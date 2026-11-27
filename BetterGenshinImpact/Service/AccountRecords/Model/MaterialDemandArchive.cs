using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>用户清理已完成需求时保存的独立历史，不参与当前需求排序和状态计算。</summary>
public sealed class MaterialDemandArchive
{
    /// <summary>完成依据使用随机合成收益时的预留率；其他依据为零。</summary>
    public int CraftingReservePercent { get; set; }
    /// <summary>库存标准物品名称。</summary>
    [JsonProperty(Required = Required.Always)]
    public string Name { get; set; } = "";
    /// <summary>归档前的完整目标及创建、人工确认信息。</summary>
    [JsonProperty(Required = Required.Always)]
    public MaterialDemand Demand { get; set; } = new();
    /// <summary>确认完成的库存来源标识。</summary>
    [JsonProperty(Required = Required.Always)]
    public string Source { get; set; } = "";
    /// <summary>归档时的来源显示名称，避免后来重命名丢失历史。</summary>
    [JsonProperty(Required = Required.Always)]
    public string SourceDisplayName { get; set; } = "";
    /// <summary>确认方式：Manual、Quantity、Delta、Family、FamilyDelta 或 FamilyEstimate。</summary>
    [JsonProperty(Required = Required.Always)]
    public string ConfirmationMethod { get; set; } = "";
    /// <summary>确认完成的证据时间。</summary>
    [JsonProperty(Required = Required.Always)]
    public DateTimeOffset CompletedAt { get; set; }
    /// <summary>用户执行归档的时间。</summary>
    [JsonProperty(Required = Required.Always)]
    public DateTimeOffset ArchivedAt { get; set; }
}
