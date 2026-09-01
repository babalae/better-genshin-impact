using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 茶包快速拖动传送（TpTaskFastDrag）专属的大地图范围内匹配定位器。
/// 把 TpTaskFastDrag 依赖的、但公版缺失的共享成员 SceneBaseMap.GetBigMapPositionInRange
/// （大图范围内匹配定位，inventory A13 / 地图扩展）能力抽成茶包版独立辅助类，使
/// TpTaskFastDrag 不再直接引用共享 SceneBaseMap 的茶包扩展成员，从而自包含
/// （PR 公版时无需改动公版共享 SceneBaseMap）。
///
/// 解耦纪律：只"搬移"不"改逻辑"。此处通过转发调用传入 SceneBaseMap 的
/// GetBigMapPositionInRange（虚拟方法，提瓦特由 TeyvatMap override 转发到分层匹配，
/// 其余地图基类返回 default 走调用方全图兜底），返回与原调用链完全相同的 256 尺度图像坐标，
/// 茶包版行为逐字节不变。
/// </summary>
public static class TpMapRegionMatch
{
    /// <summary>
    /// 按区块限定范围定位（返回 256 尺度图像坐标中心点）。
    /// 转发自 <see cref="SceneBaseMap.GetBigMapPositionInRange(Mat,Point2f,double)"/>。
    /// </summary>
    /// <param name="teyvat">大地图 SceneBaseMap（提瓦特 TeyvatMap，虚拟分派到分层区块匹配）</param>
    /// <param name="greyBigMapMat">大地图灰度图</param>
    /// <param name="genshinCenter">先验中心（原神坐标）</param>
    /// <param name="genshinRadius">先验半径（原神坐标）</param>
    /// <returns>256 尺度图像坐标中心点；本地图不支持区块限定（基类 default）或特征不足时由调用方兜底全图盲搜</returns>
    public static Point2f GetBigMapPositionInRange(SceneBaseMap teyvat, Mat greyBigMapMat, Point2f genshinCenter, double genshinRadius)
        => teyvat.GetBigMapPositionInRange(greyBigMapMat, genshinCenter, genshinRadius);
}