namespace Fischless.GameCapture.Graphics;

internal sealed class FrameCallbackLifetime
{
    private readonly object _sync = new();
    private int _activeCallbacks;
    private bool _stopping;

    public bool IsStopping
    {
        get
        {
            lock (_sync)
            {
                return _stopping;
            }
        }
    }

    public bool TryEnter()
    {
        lock (_sync)
        {
            if (_stopping)
            {
                return false;
            }

            _activeCallbacks++;
            return true;
        }
    }

    public void Exit()
    {
        lock (_sync)
        {
            if (_activeCallbacks <= 0)
            {
                throw new InvalidOperationException("No active frame callback to exit.");
            }

            _activeCallbacks--;
            if (_activeCallbacks == 0)
            {
                Monitor.PulseAll(_sync);
            }
        }
    }

    public void BeginStopAndWait()
    {
        lock (_sync)
        {
            _stopping = true;
            while (_activeCallbacks > 0)
            {
                Monitor.Wait(_sync);
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            if (_activeCallbacks != 0)
            {
                throw new InvalidOperationException("Cannot reset while frame callbacks are active.");
            }

            _stopping = false;
        }
    }
}
