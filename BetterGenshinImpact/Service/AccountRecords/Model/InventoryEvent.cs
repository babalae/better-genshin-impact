using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>可追溯的库存事件；归档保留证据，超出保留上限时才清理旧归档。</summary>
public sealed class InventoryEvent
{
    /// <summary>正增量明确上报的新增到期批次；负增量按最早到期优先推算。</summary>
    public List<InventoryExpiration>? Expirations { get; set; }
    /// <summary>UTC 创建时间加随机后缀，创建后不变。</summary>
    public string Id { get; set; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
    /// <summary>物品名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>Error、Warning、Pending、Completed、Aborted、Planned；Aborted 表示核实被人工库存替代，Planned 留待需求模块。</summary>
    public string State { get; set; } = "Pending";
    /// <summary>读取时兼容旧的颜色状态值。</summary>
    public static string NormalizeState(string state, string kind) => state switch
    {
        "Red" => "Error", "Yellow" => kind == "SourceWarning" ? "Warning" : "Pending", "Warning" when kind == "Delta" => "Pending", "Green" or "Complete" => "Completed", "Gray" => "Planned", _ => state
    };
    /// <summary>事件种类 Delta、Report、CategoryWarning、ThresholdWarning、ExpirationCheck；SourceWarning 仅兼容历史记录。</summary>
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
            // 兼容尚未存储终点字段的旧事件，仅取明确完成或归档的历史项。
            if (end == null && (Archived || State == "Completed"))
            {
                foreach (var line in History)
                {
                    var separator = line.IndexOf(' ');
                    if (separator > 0 && (line.Contains(" Green：") || line.Contains(" 已归档：")) &&
                        DateTimeOffset.TryParse(line[..separator], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    {
                        end = time;
                        break;
                    }
                }
            }
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
    /// <summary>累计推算是否与核实数量相符，空值代表尚未核实。</summary>
    public bool? VerifiedCorrect { get; set; }
    /// <summary>两行提示的第一行。</summary>
    public string Message { get; set; } = "";
    /// <summary>完整状态历史和原始输入，供日志详情查看。</summary>
    public List<string> History { get; set; } = [];
}

/// <summary>独立增量输入，人工编辑不使用这个模型。</summary>
public sealed class InventoryDeltaReport
{
    /// <summary>新增数量对应的到期批次；已有到期信息的物品增加时必须完整提供。</summary>
    public List<InventoryExpiration>? Expirations { get; set; }
    /// <summary>标准名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>有符号变动数量。</summary>
    public long Delta { get; set; }
    /// <summary>必须提供带时区的实际变动时间。</summary>
    public string RecordedAt { get; set; } = "";
}

/// <summary>一次核实上报，批次内任一红灯即记为该次上报错误。</summary>
public sealed class InventorySourceAttempt
{
    /// <summary>批次标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>关联账号。</summary>
    public string Uid { get; set; } = "";
    /// <summary>记录时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>红灯或人工反馈错误；同一批次不重复计数。</summary>
    public bool Failed { get; set; }
    /// <summary>完整错误摘要，最多保留最近二十次上报。</summary>
    public List<string> Errors { get; set; } = [];
}
