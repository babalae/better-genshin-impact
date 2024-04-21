using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map;

/// <summary>
/// 地图坐标系转换
/// 1. 原神游戏坐标系 Game
/// 2. BetterGI主地图1024区块坐标系 Main1024
/// </summary>
public class MapCoordinate
{
    public static readonly int GameMapRows = 13; // 游戏坐标下地图块的行数
    public static readonly int GameMapCols = 14; // 游戏坐标下地图块的列数
    public static readonly int GameMapUpRows = 5; // 游戏坐标下 左上角离地图原点的行数
    public static readonly int GameMapLeftCols = 7; // 游戏坐标下 左上角离地图原点的列数
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
}
