using System;

namespace BetterGenshinImpact.Service.ExternalAccess.Models;

/// <summary>
/// BetterGI 外部访问服务健康状态。
/// </summary>
public sealed class BgiHealthDto
{
    /// <summary>
    /// 服务健康状态。
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// 当前 BetterGI 实例角色。
    /// </summary>
    public required string InstanceRole { get; init; }

    /// <summary>
    /// 当前进程标识。
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// 当前实例的 UTC 启动时间。
    /// </summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>
    /// 当前实例已运行的秒数。
    /// </summary>
    public required long UptimeSeconds { get; init; }
}
