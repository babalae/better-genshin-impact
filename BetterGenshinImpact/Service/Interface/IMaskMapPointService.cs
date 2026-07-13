using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.MaskMap;

namespace BetterGenshinImpact.Service.Interface;

public interface IMaskMapPointService
{
    Task<IReadOnlyList<MaskMapPointLabel>> GetLabelCategoriesAsync(CancellationToken ct = default);

    Task<MaskMapPointsResult> GetPointsAsync(IReadOnlyList<MaskMapPointLabel> selectedItems, CancellationToken ct = default);

    Task<MaskMapPointInfo> GetPointInfoAsync(MaskMapPoint point, CancellationToken ct = default);
}
