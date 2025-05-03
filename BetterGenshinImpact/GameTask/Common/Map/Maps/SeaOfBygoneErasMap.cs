using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 旧日之海
/// 从3x4改成了1x2
/// 大地图都是半黑的,传送可能有问题
/// </summary>
public class SeaOfBygoneErasMap : SceneBaseMap
{
    #region 地图参数

    static readonly int GameMapRows = 1; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 2; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 0; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)  TODO 没找到
    static readonly int GameMapLeftCols = 0; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)  TODO 没找到

    #endregion 地图参数

    static readonly int SeaOfBygoneErasMapImageBlockWidth = 1024;

    public SeaOfBygoneErasMap() : base(type: MapTypes.SeaOfBygoneEras,
        mapSize: new Size(GameMapCols * SeaOfBygoneErasMapImageBlockWidth, GameMapRows * SeaOfBygoneErasMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * SeaOfBygoneErasMapImageBlockWidth, (GameMapUpRows + 1) * SeaOfBygoneErasMapImageBlockWidth),
        mapImageBlockWidth: SeaOfBygoneErasMapImageBlockWidth,
        splitRow: 0,
        splitCol: 0)
    {
        Layers = BaseMapLayer.LoadLayers(this);
    }

}