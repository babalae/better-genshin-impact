using System.Threading;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public interface INetworkHealthMonitor
{
    NetworkHealthSnapshot? LastSnapshot { get; }
    void RequestCheck(CancellationToken cancellationToken = default);
}
