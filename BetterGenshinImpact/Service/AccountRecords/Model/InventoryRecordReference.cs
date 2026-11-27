namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>账号内的一条批次记录引用；不承担物品身份或活动事件身份。</summary>
public sealed record InventoryRecordReference
{
    /// <summary>提交批次标识，重传时保持不变。</summary>
    public string BatchId { get; init; } = "";
    /// <summary>批次内记录标识，不能用相同标识提交不同内容。</summary>
    public string RecordId { get; init; } = "";
    /// <summary>可读组合标识，仅用于展示。</summary>
    public override string ToString() => $"{BatchId} / {RecordId}";
}
