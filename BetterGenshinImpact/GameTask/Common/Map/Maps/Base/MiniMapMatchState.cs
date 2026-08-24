using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 单个导航实例最近一次成功的小地图匹配状态。
/// 除整图图像坐标外，同时保存楼层来源，供下一帧筛选和排序分组分层候选。
/// </summary>
/// <param name="Position">匹配结果在参考底图中的图像坐标。</param>
/// <param name="Floor">匹配结果所在楼层。</param>
/// <param name="LayerId">命中层的稳定标识。</param>
/// <param name="LayerGroupId">命中层所属分组的标识。</param>
/// <param name="LayerName">命中层用于日志显示的名称。</param>
/// <param name="MapName">匹配结果所属地图，用于防止跨地图复用状态。</param>
/// <param name="IsGroupLayer">是否命中了由分组分层清单创建的层。</param>
internal sealed record MiniMapMatchState(
    Point2f Position,
    int Floor,
    string LayerId,
    string LayerGroupId,
    string LayerName,
    string MapName,
    bool IsGroupLayer);
