using BetterGenshinImpact.GameTask.AutoTrackPath;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Genshin
{
    public async Task Tp(double x, double y)
    {
        await new TpTask(CancellationContext.Instance.Cts).Tp(x, y);
    }

    public async Task Tp(string x, string y)
    {
        double.TryParse(x, out var dx);
        double.TryParse(y, out var dy);
        await Tp(dx, dy);
    }
}
