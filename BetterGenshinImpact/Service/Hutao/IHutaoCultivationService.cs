using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Hutao;

public interface IHutaoCultivationService
{
    Task<(bool Started, string Message)> FetchAndFarmAsync();

    bool IsHutaoAvailable();
}
