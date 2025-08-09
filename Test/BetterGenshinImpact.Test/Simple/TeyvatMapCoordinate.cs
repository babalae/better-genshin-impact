using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.Common.Map;

/// <summary>
/// 地图坐标系转换
/// 1. 原神游戏坐标系 Game
/// 2. BetterGI主地图1024区块坐标系 Main1024
///
/// 已过时，请使用 TeyvatMap 替代
/// </summary>
[Obsolete]
public class TeyvatMapCoordinate
{
    #region 每次地图扩大都要更新的参数

    public static readonly int GameMapRows = 13; // 游戏坐标下地图块的行数
    public static readonly int GameMapCols = 18; // 游戏坐标下地图块的列数
    public static readonly int GameMapUpRows = 5; // 游戏坐标下 左上角离地图原点的行数
    public static readonly int GameMapLeftCols = 11; // 游戏坐标下 左上角离地图原点的列数
    
    public static readonly int Main2048Width = GameMapCols * 2048;
    public static readonly int Main2048Height = GameMapRows * 2048;
    
    public static readonly int Main256Width = GameMapCols * 256;
    public static readonly int Main256Height = GameMapRows * 256;

    #endregion 每次地图扩大都要更新的参数

    public static readonly int GameMapBlockWidth = 1024; // 游戏地图块的长宽

    /// <summary>
    /// 原神游戏坐标系 -> 主地图1024区块坐标系
    /// </summary>
    /// <param name="position">[a,b,c]</param>
    /// <returns></returns>
    public static Point GameToMain1024(decimal[] position)
    {
        // 四舍六入五取偶
        var a = (int)Math.Round(position[0]); // 上
        var c = (int)Math.Round(position[2]); // 左

        // 转换1024区块坐标，大地图坐标系正轴是往左上方向的
        // 这里写最左上角的区块坐标(GameMapUpRows,GameMapLeftCols)/(上,左),截止4.5版本，最左上角的区块坐标是(5,7)

        return new Point((GameMapLeftCols + 1) * GameMapBlockWidth - c, (GameMapUpRows + 1) * GameMapBlockWidth - a);
    }

    /// <summary>
    /// 主地图1024区块坐标系 -> 原神游戏坐标系
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public static Point Main1024ToGame(Point point)
    {
        return new Point((GameMapLeftCols + 1) * GameMapBlockWidth - point.X, (GameMapUpRows + 1) * GameMapBlockWidth - point.Y);
    }

    /// <summary>
    /// 原神游戏坐标系 -> 主地图2048区块坐标系
    /// </summary>
    /// <param name="position">[a,b,c]</param>
    /// <returns></returns>
    public static Point GameToMain2048(decimal[] position)
    {
        var a = position[0]; // 上
        var c = position[2]; // 左

        // 转换1024区块坐标，大地图坐标系正轴是往左上方向的
        // 这里写最左上角的区块坐标(GameMapUpRows,GameMapLeftCols)/(上,左),截止4.5版本，最左上角的区块坐标是(5,7)

        return new Point((int)(((GameMapLeftCols + 1) * GameMapBlockWidth - c) * 2), (int)(((GameMapUpRows + 1) * GameMapBlockWidth - a) * 2));
    }

    /// <summary>
    /// 原神游戏坐标系 -> 主地图2048区块坐标系
    /// </summary>
    /// <param name="point">(c,a)</param>
    /// <returns></returns>
    public static Point GameToMain2048(Point point)
    {
        return new Point(((GameMapLeftCols + 1) * GameMapBlockWidth - point.X) * 2, ((GameMapUpRows + 1) * GameMapBlockWidth - point.Y) * 2);
    }

    /// <summary>
    /// 原神游戏坐标系 -> 主地图2048区块坐标系
    /// </summary>
    /// <returns></returns>
    public static (double x, double y) GameToMain2048(double c, double a)
    {
        // 转换1024区块坐标，大地图坐标系正轴是往左上方向的
        // 这里写最左上角的区块坐标(GameMapUpRows,GameMapLeftCols)/(上,左),截止4.5版本，最左上角的区块坐标是(5,7)

        return new(((GameMapLeftCols + 1) * GameMapBlockWidth - c) * 2, ((GameMapUpRows + 1) * GameMapBlockWidth - a) * 2);
    }

    public static Rect GameToMain2048(Rect rect)
    {
        var center = rect.GetCenterPoint();
        // 转换中心点坐标
        (double newX, double newY) = GameToMain2048(center.X, center.Y);

        // 返回转换后的矩形坐标
        return new Rect((int)Math.Round(newX) - rect.Width, (int)Math.Round(newY) - rect.Height, rect.Width * 2, rect.Height * 2);
    }

    /// <summary>
    /// 主地图2048区块坐标系 -> 原神游戏坐标系
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public static Point Main2048ToGame(Point point)
    {
        return new Point((GameMapLeftCols + 1) * GameMapBlockWidth - point.X / 2, (GameMapUpRows + 1) * GameMapBlockWidth - point.Y / 2);
    }

    public static Point2f Main2048ToGame(Point2f point)
    {
        return new Point2f((GameMapLeftCols + 1) * GameMapBlockWidth - point.X / 2f, (GameMapUpRows + 1) * GameMapBlockWidth - point.Y / 2f);
    }

    /// <summary>
    /// 主地图2048区块坐标系 -> 原神游戏坐标系
    /// </summary>
    /// <param name="rect"></param>
    /// <returns></returns>
    public static Rect Main2048ToGame(Rect rect)
    {
        var center = rect.GetCenterPoint();
        var point = Main2048ToGame(center);
        return new Rect(point.X - rect.Width / 4, point.Y - rect.Height / 4, rect.Width / 2, rect.Height / 2);
    }
}
