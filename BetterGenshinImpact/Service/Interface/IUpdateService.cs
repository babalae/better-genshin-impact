using BetterGenshinImpact.Model;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Interface;

public interface IUpdateService
{
    /// <summary>
    /// I will check the update and completed the update if needed
    /// </summary>
    /// <returns></returns>
    public Task CheckUpdateAsync(UpdateOption option);
}
