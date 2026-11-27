using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>一次库存操作的历史；同批各条独立接受或拒绝，不代替当前库存状态。</summary>
public sealed class InventoryBatch
{
    /// <summary>批次文件格式。</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>账号目录内的批次标识。</summary>
    public string BatchId { get; set; } = "";
    /// <summary>Report、Delta、Import、Manual、Failure 或 Mixed；导入可包含多种记录。</summary>
    public string Kind { get; set; } = "";
    /// <summary>接收时间，仅用于追溯，不参与业务时间判断。</summary>
    public DateTimeOffset ReceivedAt { get; set; }
    /// <summary>包含已清理记录的原始条数。</summary>
    public int OriginalCount { get; set; }
    /// <summary>因保留上限清理的记录数。</summary>
    public int RemovedCount { get; set; }
    /// <summary>本次处理的记录索引；仅用于服务内查找，不参与持久化，记录顺序仍由列表维护。</summary>
    [JsonIgnore]
    internal Dictionary<string, InventoryBatchRecord> RecordIndex { get; set; } = new(StringComparer.Ordinal);

    /// <summary>当前保留的批次记录。</summary>
    public List<InventoryBatchRecord> Records { get; set; } = [];
}

/// <summary>批次输入的持久去重凭证；清理历史不允许重新累计已接受输入。</summary>
public sealed class InventoryBatchReceipt
{
    /// <summary>所属物品，明确删除物品时沿用原规则重置接收状态。</summary>
    public string Name { get; set; } = "";
    /// <summary>首次输入摘要。</summary>
    public string ContentHash { get; set; } = "";
    /// <summary>该输入是否已成功处理，包括已判定重复。</summary>
    public bool Accepted { get; set; }
}

/// <summary>按物品查询历史时返回的轻量索引行，可从批次文件重建。</summary>
public sealed class InventoryBatchIndexEntry
{
    /// <summary>批次记录位置。</summary>
    public InventoryRecordReference Reference { get; set; } = new();
    /// <summary>用于物品索引的标准或原始名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>最近处理时间，用于排序和历史裁剪。</summary>
    public DateTimeOffset ProcessedAt { get; set; }
}
