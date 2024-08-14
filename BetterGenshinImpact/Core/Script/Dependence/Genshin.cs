using BetterGenshinImpact.GameTask.AutoTrackPath;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Genshin
{
    public async Task Tp(double x, double y)
    {
        await new TpTask(CancellationContext.Instance.Cts).Tp(x, y);
    }
}
