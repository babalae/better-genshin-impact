namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>库存数量或临期天数规则，不执行脚本。</summary>
public sealed class InventoryThresholdRule
{
    /// <summary>稳定规则标识，修改判断条件后视为新一轮规则。</summary>
    public string Id { get; set; } = "";
    /// <summary>是否启用，示例规则默认关闭。</summary>
    public bool Enabled { get; set; }
    /// <summary>准确的中文标准物品名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>QuantityBelow 为库存不足，ExpiresWithinDays 为临期或过期。</summary>
    public string Kind { get; set; } = "QuantityBelow";
    /// <summary>严格小于此数量或天数时触发，必须为正数。</summary>
    public decimal Threshold { get; set; } = 1;
    /// <summary>本条规则独立的提示语；空值时按本条条件生成默认说明。</summary>
    public string Message { get; set; } = "";
}

/// <summary>按 UID 保存条件持续期，不依赖可能被清理的归档记录。</summary>
public sealed class InventoryThresholdState
{
    /// <summary>规则对应物品，删除物品时清理轮次，不依赖归档仍存在。</summary>
    public string Name { get; set; } = "";
    /// <summary>当前条件定义的序列化签名。</summary>
    public string Signature { get; set; } = "";
    /// <summary>本轮提醒事件 ID；条件持续成立时即使归档也不重新创建。</summary>
    public string EventId { get; set; } = "";
}
