using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

public interface IIndependentMap
{
    /// <summary>
    /// 获取大地图在整张地图上的位置
    /// </summary>
    /// <param name="greyBigMapMat"></param>
    /// <returns></returns>
    Point2f GetBigMapPosition(Mat greyBigMapMat);

    /// <summary>
    /// 获取大地图在整张地图上的位置（矩形，含缩放信息）
    /// </summary>
    /// <param name="greyBigMapMat"></param>
    /// <returns></returns>
    Rect GetBigMapRect(Mat greyBigMapMat);

    /// <summary>
    /// 获取小地图在整张地图上的位置
    /// </summary>
    /// <param name="greyMiniMapMat"></param>
    /// <returns></returns>
    Point2f GetMiniMapPosition(Mat greyMiniMapMat);

    /// <summary>
    /// 获取小地图在整张地图上的位置
    /// 根据上一个位置的坐标缩小匹配范围，加速获取速度
    /// </summary>
    /// <param name="greyMiniMapMat"></param>
    /// <param name="prevX"></param>
    /// <param name="prevY"></param>
    /// <returns></returns>
    Point2f GetMiniMapPosition(Mat greyMiniMapMat, float prevX, float prevY);
}