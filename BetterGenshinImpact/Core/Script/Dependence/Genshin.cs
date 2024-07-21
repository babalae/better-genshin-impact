using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoTrackPath;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Genshin
{
    public async Task Tp(double x, double y)
    {
        await new TpTask(CancellationContext.Instance.Cts).Tp(x, y);
    }
}
