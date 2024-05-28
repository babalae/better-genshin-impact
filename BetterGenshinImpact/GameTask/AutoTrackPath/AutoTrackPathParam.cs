using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public class AutoTrackPathParam : BaseTaskParam
{
    public AutoTrackPathParam(CancellationTokenSource cts) : base(cts)
    {
    }
}
