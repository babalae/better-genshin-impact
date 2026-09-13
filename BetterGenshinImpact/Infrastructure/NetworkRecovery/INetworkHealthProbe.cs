using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public interface INetworkHealthProbe
{
    ValueTask<NetworkProbeResult> ProbeAsync(string target, int timeoutMilliseconds, CancellationToken cancellationToken);
}
