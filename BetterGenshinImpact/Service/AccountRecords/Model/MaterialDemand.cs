using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>单个物品的目标库存；需求独立于库存事件保存。</summary>
public sealed class MaterialDemand
{
    /// <summary>界面创建或修改目标的稳定来源标识。</summary>
    public const string ManualSourceReference = "AccountRecords.SetDemandAsync";
    /// <summary>本次需求版本，编辑与删除时用于拒绝过期界面操作。</summary>
    [JsonProperty(Required = Required.Always)]
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>目标库存总量，不是需要额外采集的数量。</summary>
    [JsonProperty(Required = Required.Always)]
    public long TargetQuantity { get; set; }
    /// <summary>目标登记来源；新建对象默认人工创建，读取文件时必须显式提供。</summary>
    [JsonProperty(Required = Required.Always)]
    public string Source { get; set; } = ManualSourceReference;
    /// <summary>创建或修改目标的登记时间，用于展示和备份。</summary>
    [JsonProperty(Required = Required.Always)]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>人工确认本次需求完成的时间；不修改库存数量。</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }
}
