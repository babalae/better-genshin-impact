using BetterGenshinImpact.Model;
using System.Threading;

namespace BetterGenshinImpact.Core.Script;

public class CancellationContext : Singleton<CancellationContext>
{
    public CancellationTokenSource Cts { get; set; } = new();
    public bool IsManualStop { get; private set; }
    public bool IsCancellationRequested => !disposed && Cts.IsCancellationRequested;

    private bool disposed;

    public void Set()
    {
        Cts = new CancellationTokenSource();
        IsManualStop = false;
        disposed = false;
    }

    public void ManualCancel()
    {
        if (!disposed)
        {
            IsManualStop = true;
            Cts.Cancel();
        }
    }

    public void Cancel()
    {
        if (!disposed)
        {
            Cts.Cancel();
        }
    }

    public void Clear()
    {
        Cts.Dispose();
        disposed = true;
    }
}
