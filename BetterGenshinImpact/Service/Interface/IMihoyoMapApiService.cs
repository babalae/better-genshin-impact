using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using BetterGenshinImpact.Service.Model.MihoyoMap.Responses;

namespace BetterGenshinImpact.Service.Interface
{
    public interface IMihoyoMapApiService
    {
        Task<ApiResponse<LabelTreeData>> GetLabelTreeAsync(LabelTreeRequest request, CancellationToken ct = default);
        Task<ApiResponse<PointListData>> GetPointListAsync(PointListRequest request, CancellationToken ct = default);
        Task<ApiResponse<PointListData>> GetPointListCacheAsync(PointListRequest request, CancellationToken ct = default);
    }
}
