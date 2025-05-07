namespace Fischless.GameCapture;

public abstract class CaptureSession : IDisposable
{
    private int _refCount;

    protected abstract void DisposeInternal();

    public void Reference()
    {
        Interlocked.Increment(ref _refCount);
    }

    public void Dispose()
    {
        if (Interlocked.Decrement(ref _refCount) <= 0)
        {
            DisposeInternal();
        }

        GC.SuppressFinalize(this);
    }
}
