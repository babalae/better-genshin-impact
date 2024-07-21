using BetterGenshinImpact.Model;
using System.Threading;

namespace BetterGenshinImpact.Core.Script;

public class CancellationContext : Singleton<CancellationContext>
{
    public CancellationTokenSource Cts { get; set; } = new();

    public void Set()
    {
        Cts = new CancellationTokenSource();
    }

    public void Cancel()
    {
        Cts.Cancel();
    }

    public void Clear()
    {
        Cts.Dispose();
    }
}
