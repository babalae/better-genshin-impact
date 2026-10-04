using Serilog.Events;
using System;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.ExternalAccess.Logging;

/// <summary>
/// 通过 WebSocket 对外传输的一条结构化日志事件。
/// </summary>
public sealed class ExternalLogEvent
{
    /// <summary>
    /// 消息类型。
    /// </summary>
    public string Type { get; init; } = "log";

    /// <summary>
    /// 进程内单调递增的日志序号。
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>
    /// 日志产生的 UTC 时间。
    /// </summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// 日志级别名称。
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// 日志来源上下文。
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// 已渲染的日志正文。
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 异常详情；没有异常时为空。
    /// </summary>
    public string? Exception { get; init; }

    /// <summary>
    /// 产生该日志的 BetterGI 实例标识。
    /// </summary>
    public required string Instance { get; init; }

    /// <summary>
    /// 用于服务端过滤的 Serilog 日志级别，不写入外部 JSON。
    /// </summary>
    [JsonIgnore]
    public required LogEventLevel LevelValue { get; init; }
}
