using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.ExternalAccess.Models;
using BetterGenshinImpact.Service.Instance;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.ExternalAccess;

/// <summary>
/// 提供可由多种外部传输协议复用的 BetterGI 基础能力。
/// </summary>
public sealed class BgiExternalCapabilityService(InstanceService instanceService) : IBgiExternalCapabilityService
{
    /// <inheritdoc />
    public Task<BgiHealthDto> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = instanceService.Context;
        var uptime = DateTimeOffset.UtcNow - context.StartedAt;
        return Task.FromResult(new BgiHealthDto
        {
            Status = "healthy",
            InstanceRole = context.InstanceType.ToString(),
            ProcessId = context.ProcessId,
            StartedAtUtc = context.StartedAt.ToUniversalTime(),
            UptimeSeconds = Math.Max(0, (long)uptime.TotalSeconds)
        });
    }

    /// <inheritdoc />
    public Task<BgiVersionDto> GetVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BgiVersionDto
        {
            Product = "BetterGI",
            Version = Global.Version,
            ApiVersion = "v1"
        });
    }
}
