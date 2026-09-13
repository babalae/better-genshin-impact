namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public interface INetworkPauseGate
{
    bool IsNetworkPaused { get; }
    NetworkHealthSnapshot? LastFailure { get; }
    void EnterNetworkPause(NetworkHealthSnapshot snapshot);
    void ClearNetworkPause();
}
