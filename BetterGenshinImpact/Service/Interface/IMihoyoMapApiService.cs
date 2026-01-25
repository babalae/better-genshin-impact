using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using BetterGenshinImpact.Service.Model.MihoyoMap.Responses;

namespace BetterGenshinImpact.Service.Interface
{
    public interface IMihoyoMapApiService
    {
        /**
         * 获取点位类型树
         */
        Task<ApiResponse<LabelTreeData>> GetLabelTreeAsync(LabelTreeRequest request, CancellationToken ct = default);
        /**
         * 获取点位信息明细
         */
        Task<ApiResponse<PointInfoData>> GetPointInfoAsync(PointInfoRequest request, CancellationToken ct = default);
        /**
         * 通过父类型获取所有点位
         */
        Task<ApiResponse<PointListData>> GetPointListAsync(PointListRequest request, CancellationToken ct = default);
    }
}
