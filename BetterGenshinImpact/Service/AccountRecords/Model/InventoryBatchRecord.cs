using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>一条原始输入及最近处理结果；当前灯状态仍由活动事件负责。</summary>
public sealed class InventoryBatchRecord
{
    /// <summary>本次是已成功处理输入的重传，不重新发布或复活已清理历史。</summary>
    [JsonIgnore]
    internal bool AlreadyAccepted { get; set; }
    /// <summary>批次内标识。</summary>
    public string RecordId { get; set; } = "";
    /// <summary>CSV 标准名称或被拒绝的原始名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>输入来源。</summary>
    public string Source { get; set; } = "";
    /// <summary>具体输入类型，导入批次可以包含总量和增量。</summary>
    public string Kind { get; set; } = "";
    /// <summary>完整原始输入，包含未通过时间校验的文本。</summary>
    public JObject Input { get; set; } = new();
    /// <summary>首次提交的内容摘要，历史裁剪后仍用于身份冲突检查。</summary>
    public string ContentHash { get; set; } = "";
    /// <summary>Accepted、Rejected 或 Duplicate；不等同于增量已核实。</summary>
    public string Outcome { get; set; } = "";
    /// <summary>本条处理说明。</summary>
    public string Message { get; set; } = "";
    /// <summary>最近一次处理时间，失败输入允许按原业务规则重新校验。</summary>
    public DateTimeOffset ProcessedAt { get; set; }
    /// <summary>人工确认前的有效数量；无基准时为空。</summary>
    public long? PreviousQuantity { get; set; }
    /// <summary>人工确认的实际提交时间，不纳入重传输入摘要。</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }
}

