using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>某来源最后一次被接受的库存记录；队首代表当前库存。</summary>
public sealed class InventoryRecord
{
    /// <summary>可选的分批到期信息；未提供的数量不推断为永久有效。</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<InventoryExpiration>? Expirations { get; set; }
    /// <summary>核实来源的一次上报标识；人工编辑为空。</summary>
    public string AttemptId { get; set; } = "";
    /// <summary>非负库存数量。</summary>
    public long Quantity { get; set; }
    /// <summary>库存数量实际采集或确认的时间，统一保存为 UTC，导入时不刷新。</summary>
    public DateTimeOffset RecordedAt { get; set; }
    /// <summary>公共来源字典中的标识。</summary>
    public string Source { get; set; } = "";
}

/// <summary>批量上报的一项；必须携带库存数量实际采集或确认的时间。</summary>
public sealed class InventoryReport
{
    /// <summary>可选的数量与到期时间列表，随本次记录保存，不沿用其他记录的到期信息。</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<InventoryExpiration>? Expirations { get; set; }
    /// <summary>标准物品名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>可选的来源分类，仅用于与本地分类核对，不覆盖本地字典。</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Category { get; set; }
    /// <summary>来源提交的数量。</summary>
    public long Quantity { get; set; }
    /// <summary>必填的实际采集或确认时间，须带时区；不是发送、接收或导入时间，无效值作为异常保留。</summary>
    public string? RecordedAt { get; set; }
}

/// <summary>只保存尚未处理的库存异常，和有效库存分开管理。</summary>
public sealed class InventoryError
{
    /// <summary>可供调用方区分的异常类型。</summary>
    public string Code { get; set; } = "Validation";
    /// <summary>处理凭证，防止旧弹窗处理后来产生的新异常。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>异常原因及手动修订提示。</summary>
    public string Message { get; set; } = "";
    /// <summary>发生时间。</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>上报来源。</summary>
    public string Source { get; set; } = "";
    /// <summary>原始上报，供修订和追溯。</summary>
    public InventoryReport Incoming { get; set; } = new();
    /// <summary>冲突发生时的有效记录快照。</summary>
    public InventoryRecord? Current { get; set; }
    /// <summary>历史接受标记；新错误固定为 false，服务端不再允许接受错误记录。</summary>
    public bool CanAccept { get; set; }
}

/// <summary>批量写入结果，让上报方知道哪些条目需要用户处理。</summary>
public sealed class InventoryReportResult
{
    /// <summary>不阻断数量处理的分类提醒，调用方应与拒绝结果分别展示。</summary>
    public Dictionary<string, string> Warnings { get; set; } = new(StringComparer.Ordinal);
    /// <summary>本次忽略的重复输入数量，不作为新增异常。</summary>
    public int DuplicateCount { get; set; }
    /// <summary>重复输入中仍关联未处理错误的数量。</summary>
    public int DuplicateErrorCount { get; set; }
    /// <summary>正常接受的物品名称。</summary>
    public List<string> Accepted { get; set; } = [];
    /// <summary>被拒绝的名称和可读原因。</summary>
    public Dictionary<string, string> Rejected { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>单账号的库存文件；两个逻辑表一起原子保存，避免库存与异常不同步。</summary>
public sealed class InventoryDocument
{
    /// <summary>本次读取是否已合并归档；保存前补齐，避免错误计算保留数量。</summary>
    [JsonIgnore]
    internal bool EventArchiveLoaded { get; set; }

    /// <summary>当前 UID 置顶的物品名称；仅影响本地展示，不随来源上报覆盖。</summary>
    public HashSet<string> Prior { get; set; } = new(StringComparer.Ordinal);
    /// <summary>阈值规则当前触发轮次，归档后仍保留直到条件明确解除。</summary>
    public Dictionary<string, InventoryThresholdState> ThresholdStates { get; set; } = new();
    /// <summary>亮灯事件与归档历史，跟库存一起原子保存。</summary>
    public List<InventoryEvent> Events { get; set; } = [];
    /// <summary>清理历史后保留的去重凭证，按物品分组；删除物品时一并重置。</summary>
    public Dictionary<string, HashSet<string>> EventReceipts { get; set; } = new(StringComparer.Ordinal);
    /// <summary>文件格式版本。</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>账号标识。</summary>
    public string Uid { get; set; } = "";
    /// <summary>标准名称到最近来源记录队列。</summary>
    public Dictionary<string, List<InventoryRecord>> Items { get; set; } = new(StringComparer.Ordinal);
    /// <summary>标准名称或未通过校验的名称到当前异常。</summary>
    public Dictionary<string, InventoryError> Errors { get; set; } = new(StringComparer.Ordinal);
    /// <summary>本地最后修改时间，用于快照分日，不替换物品记录时间。</summary>
    public DateTimeOffset? ModifiedAt { get; set; }
}
