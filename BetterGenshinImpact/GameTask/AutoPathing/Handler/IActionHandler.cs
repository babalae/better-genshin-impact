using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public interface IActionHandler
{
    Task RunAsync(CancellationTokenSource cts);
}
