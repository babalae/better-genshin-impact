using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask;

public interface ISoloTask
{
    Task Start(CancellationTokenSource cts);
}
