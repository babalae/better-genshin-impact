using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Script.Dependence;

public class GlobalMethod
{
    public static async Task Sleep(int millisecondsTimeout)
    {
        await Task.Delay(millisecondsTimeout);
    }
}
