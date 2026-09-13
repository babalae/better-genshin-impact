using System;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public enum NetworkHealthStatus
{
    Healthy,
    Jitter,
    DnsFailure,
    Unreachable
}

public readonly record struct NetworkProbeResult(
    bool IsHealthy,
    NetworkHealthStatus Status,
    TimeSpan Latency,
    string? ErrorMessage = null);

public readonly record struct NetworkHealthSnapshot(
    DateTimeOffset CheckedAt,
    string Target,
    NetworkHealthStatus Status,
    int ConsecutiveFailures,
    TimeSpan Latency,
    string? ErrorMessage = null)
{
    public bool IsHealthy => Status == NetworkHealthStatus.Healthy;
}

public static class NetworkHealthDecisions
{
    public static NetworkHealthSnapshot CreateSnapshot(
        NetworkProbeResult probe,
        string target,
        int previousFailures,
        DateTimeOffset checkedAt)
    {
        var failures = probe.IsHealthy ? 0 : previousFailures + 1;
        return new NetworkHealthSnapshot(
            checkedAt,
            target,
            probe.Status,
            failures,
            probe.Latency,
            probe.ErrorMessage);
    }

    public static bool ShouldPause(NetworkHealthSnapshot snapshot, int failureThreshold)
    {
        return !snapshot.IsHealthy && snapshot.ConsecutiveFailures >= Math.Max(1, failureThreshold);
    }
}
