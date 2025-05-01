using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

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

    #region 坐标系转换

    /// <summary>
    /// 地图图像坐标系 -> 原神游戏坐标系
    /// </summary>
    /// <param name="imageCoordinates"></param>
    /// <returns></returns>
    Point2f ConvertImageCoordinatesToGenshinMapCoordinates(Point2f imageCoordinates);

    /// <summary>
    /// 原神游戏坐标系 -> 地图图像坐标系
    /// </summary>
    /// <param name="genshinMapCoordinates"></param>
    /// <returns></returns>
    Point2f ConvertGenshinMapCoordinatesToImageCoordinates(Point2f genshinMapCoordinates);

    (float x, float y) ConvertGenshinMapCoordinatesToImageCoordinates(float c, float a);

    #endregion
}