using BetterGenshinImpact.Model;
using System.Threading;

namespace BetterGenshinImpact.Core.Script;

public class CancellationContext : Singleton<CancellationContext>
{
    public CancellationTokenSource Cts { get; set; } = new();
    public bool IsManualStop { get; set; }

    private bool disposed;

    public void Set()
    {
        Cts = new CancellationTokenSource();
        IsManualStop = false;
        disposed = false;
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
