using System.Threading;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public interface IPauseCoordinator
{
    bool IsPaused { get; }
    void ToggleManualPause();
    void WaitIfPaused(CancellationToken cancellationToken = default);
}
