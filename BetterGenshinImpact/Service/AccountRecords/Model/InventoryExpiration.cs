using System;
using System.Globalization;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>一次库存记录中具有相同到期时间的一批数量。</summary>
public sealed class InventoryExpiration
{
    /// <summary>该批数量，必须为正整数。</summary>
    public long Quantity { get; set; }
    /// <summary>带时区的到期时间，写入时规范化为 UTC；缺省不允许。</summary>
    public string ExpiresAt { get; set; } = "";
    /// <summary>经存储层校验后供展示和阈值计算使用的到期时刻。</summary>
    [JsonIgnore]
    public DateTimeOffset Time => DateTimeOffset.Parse(ExpiresAt, CultureInfo.InvariantCulture);
}
