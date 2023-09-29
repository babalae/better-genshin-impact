using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoThrowRodTask : BaseTaskThread
    {
        public AutoThrowRodTask(CancellationTokenSource cts) : base(cts)
        {
        }
    }
}
