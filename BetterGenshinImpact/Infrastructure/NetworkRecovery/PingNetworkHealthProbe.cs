using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class PingNetworkHealthProbe : INetworkHealthProbe
{
    public async ValueTask<NetworkProbeResult> ProbeAsync(
        string target,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new NetworkProbeResult(false, NetworkHealthStatus.DnsFailure, TimeSpan.Zero, "探测地址为空");
        }

        using var ping = new Ping();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var reply = await ping.SendPingAsync(
                target.Trim(),
                Math.Clamp(timeoutMilliseconds, 100, 30_000))
                .WaitAsync(cancellationToken);
            stopwatch.Stop();

            if (reply.Status == IPStatus.Success)
            {
                return new NetworkProbeResult(true, NetworkHealthStatus.Healthy, stopwatch.Elapsed);
            }

            var status = reply.Status == IPStatus.TimedOut
                ? NetworkHealthStatus.Jitter
                : NetworkHealthStatus.Unreachable;
            return new NetworkProbeResult(false, status, stopwatch.Elapsed, reply.Status.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException e) when (IsDnsFailure(e))
        {
            stopwatch.Stop();
            return new NetworkProbeResult(false, NetworkHealthStatus.DnsFailure, stopwatch.Elapsed, e.Message);
        }
        catch (PingException e) when (e.InnerException is SocketException socketException && IsDnsFailure(socketException))
        {
            stopwatch.Stop();
            return new NetworkProbeResult(false, NetworkHealthStatus.DnsFailure, stopwatch.Elapsed, e.Message);
        }
        catch (Exception e)
        {
            stopwatch.Stop();
            return new NetworkProbeResult(false, NetworkHealthStatus.Unreachable, stopwatch.Elapsed, e.Message);
        }
    }

    private static bool IsDnsFailure(SocketException exception)
    {
        return exception.SocketErrorCode is SocketError.HostNotFound
            or SocketError.NoData
            or SocketError.TryAgain;
    }
}
