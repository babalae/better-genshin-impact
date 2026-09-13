using System;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class NetworkPauseGate : INetworkPauseGate
{
    private readonly object _sync = new();
    private bool _isNetworkPaused;
    private NetworkHealthSnapshot? _lastFailure;

    public bool IsNetworkPaused
    {
        get
        {
            lock (_sync)
            {
                return _isNetworkPaused;
            }
        }
    }

    public NetworkHealthSnapshot? LastFailure
    {
        get
        {
            lock (_sync)
            {
                return _lastFailure;
            }
        }
    }

    public void EnterNetworkPause(NetworkHealthSnapshot snapshot)
    {
        lock (_sync)
        {
            _lastFailure = snapshot;
            _isNetworkPaused = true;
        }
    }

    public void ClearNetworkPause()
    {
        lock (_sync)
        {
            _isNetworkPaused = false;
            _lastFailure = null;
        }
    }
}
