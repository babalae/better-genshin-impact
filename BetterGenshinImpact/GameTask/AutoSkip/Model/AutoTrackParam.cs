using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoSkip.Model;

public class AutoTrackParam : BaseTaskParam
{
    public AutoTrackParam(CancellationTokenSource cts) : base(cts)
    {
    }
}
