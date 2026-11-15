using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.Service.Interface;

public interface IMaskMapPointService
{
    Task<IReadOnlyList<MaskMapPointLabel>> GetLabelCategoriesAsync(CancellationToken ct = default);

    Task<MaskMapPointsResult> GetPointsAsync(IReadOnlyList<MaskMapPointLabel> selectedItems, CancellationToken ct = default,
        MapTypes mapType = MapTypes.Teyvat);

    Task<MaskMapPointInfo> GetPointInfoAsync(MaskMapPoint point, CancellationToken ct = default);
}
