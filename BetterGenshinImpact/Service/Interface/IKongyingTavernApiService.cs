using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Model.Oauth;
using BetterGenshinImpact.Service.Tavern.Model;

namespace BetterGenshinImpact.Service.Interface
{
    public interface IKongyingTavernApiService
    {
        Task<OauthTokenResponse> GetTokenAsync(CancellationToken ct = default);

        Task<IReadOnlyList<ItemTypeVo>> GetItemTypeListAsync(CancellationToken ct = default);

        Task<IReadOnlyList<MarkerVo>> GetMarkerListAsync(CancellationToken ct = default);

        Task<IReadOnlyList<IconVo>> GetIconListAsync(CancellationToken ct = default);
    }
}
