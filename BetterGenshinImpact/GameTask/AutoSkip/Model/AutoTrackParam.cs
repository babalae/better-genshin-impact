using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoSkip.Model;

public class AutoTrackParam : BaseTaskParam
{
    public AutoTrackParam(CancellationTokenSource cts) : base(cts)
    {
    }
}
