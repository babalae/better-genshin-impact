using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>可追溯的库存事件；开发期全部保留，归档只改变活动状态。</summary>
public sealed class InventoryEvent
{
    /// <summary>产生此活动事件的输入；一条输入可产生多个事件，事件 ID 仍独立。</summary>
    public InventoryRecordReference? RecordReference { get; set; }
    /// <summary>核实或人工替代此事件的具体记录。</summary>
    public InventoryRecordReference? VerificationReference { get; set; }
    /// <summary>本次事务中是否需要保存归档变化；读取归档后清除，新事件默认需要保存。</summary>
    [JsonIgnore]
    internal bool ArchiveDirty { get; set; } = true;
    /// <summary>读取归档时所在日期，用于状态变化后移动归档日期。</summary>
    [JsonIgnore]
    internal string? ArchiveFileDate { get; set; }
    /// <summary>展示时的内容版本；不落盘，能捕获原 ID 上的任何内容更新。</summary>
    [JsonIgnore]
    public string ContentVersion => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this))));
    /// <summary>增量原始时间是否通过校验。</summary>
    public bool HasValidRecordedAt { get; set; }
    /// <summary>正增量明确上报的新增到期批次；负增量按最早到期优先推算。</summary>
    public List<InventoryExpiration>? Expirations { get; set; }
    /// <summary>UTC 创建时间加随机后缀，创建后不变。</summary>
    public string Id { get; set; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
    /// <summary>物品名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>Error、Warning、Pending、Completed、Aborted、Planned；Aborted 表示核实被人工库存替代，Planned 留待需求模块。</summary>
    public string State { get; set; } = "Pending";
    /// <summary>事件种类 Delta、Report、CategoryWarning、ExpirationCheck、VerifiedErrorWarning。</summary>
    public string Kind { get; set; } = "Report";
    /// <summary>核实上报批次，用于用户反馈去重。</summary>
    public string AttemptId { get; set; } = "";
    /// <summary>对应库存错误的稳定标识；业务关联不依赖历史说明文本。</summary>
    public string ErrorId { get; set; } = "";
    /// <summary>失败输入的稳定指纹，删除物品后不再参与去重。</summary>
    public string ReportFingerprint { get; set; } = "";
    /// <summary>重复失败输入或同来源分类提醒的累计接收次数，不重复增加历史行。</summary>
    public long RepeatCount { get; set; } = 1;
    /// <summary>最近一次收到重复失败输入或分类提醒的 UTC 时间。</summary>
    public DateTimeOffset? LastReceivedAt { get; set; }
    /// <summary>是否已归档，可由用户操作或系统状态更新设置，不等同于核实成功。</summary>
    public bool Archived { get; set; }
    /// <summary>物品已被明确删除；历史保留，但不参与重建后的增量去重。</summary>
    public bool ItemDeleted { get; set; }
    /// <summary>是否保留完成后的绿灯提示。</summary>
    public bool KeepNotice { get; set; }
    /// <summary>输入来源。</summary>
    public string Source { get; set; } = "";
    /// <summary>输入记录时间，增量去重依据。</summary>
    public DateTimeOffset RecordedAt { get; set; }
    /// <summary>事件完成核实或归档的时间；后续归档不覆盖已完成时间。</summary>
    public DateTimeOffset? EndedAt { get; set; }
    /// <summary>列表展示本地时区的事件起止时间，不写入存储。</summary>
    [JsonIgnore]
    public string TimeRange
    {
        get
        {
            var start = Id.Length >= 22 && DateTimeOffset.TryParseExact(Id[..22], "yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var created) ? created : RecordedAt;
            var end = EndedAt;
            return end is { } finished
                ? $"{start.ToLocalTime():yyyy-MM-dd HH:mm:ss} → {finished.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                : $"{start.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
    }
    /// <summary>增量值或原始上报数量。</summary>
    public long Quantity { get; set; }
    /// <summary>事件创建时的确认库存。</summary>
    public long? Baseline { get; set; }
    /// <summary>核实来源。</summary>
    public string VerificationSource { get; set; } = "";
    /// <summary>同一轮核实标识，避免把先前已完成的变动加入本轮摘要。</summary>
    public string VerificationId { get; set; } = "";
    /// <summary>累计推算是否与核实数量相符；空值代表尚未核实或缺少原库存基准、无法比对。</summary>
    public bool? VerifiedCorrect { get; set; }
    /// <summary>两行提示的第一行。</summary>
    public string Message { get; set; } = "";
    /// <summary>状态变化说明；原始输入通过 RecordReference 查询批次。</summary>
    public List<string> History { get; set; } = [];
}

/// <summary>独立增量输入，人工编辑不使用这个模型。</summary>
public sealed class InventoryDeltaReport
{
    /// <summary>可选批次内标识，重传时应保持不变。</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? RecordId { get; set; }
    /// <summary>新增数量对应的到期批次；已有到期信息的物品增加时必须完整提供。</summary>
    public List<InventoryExpiration>? Expirations { get; set; }
    /// <summary>标准名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>有符号变动数量。</summary>
    public long Delta { get; set; }
    /// <summary>必须提供带时区的实际变动时间。</summary>
    public string RecordedAt { get; set; } = "";
}
