using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 旧日之海
/// </summary>
public class SeaOfBygoneErasMap : IndependentBaseMap
{
    #region 地图参数

    static readonly int GameMapRows = 3; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 3; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 1; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    static readonly int GameMapLeftCols = 1; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 地图参数

    static readonly int SeaOfBygoneErasMapImageBlockWidth = 1024;

    public SeaOfBygoneErasMap() : base(type: IndependentMapTypes.SeaOfBygoneEras,
        mapSize: new Size(GameMapCols * SeaOfBygoneErasMapImageBlockWidth, GameMapRows * SeaOfBygoneErasMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * SeaOfBygoneErasMapImageBlockWidth, (GameMapUpRows + 1) * SeaOfBygoneErasMapImageBlockWidth),
        mapImageBlockWidth: SeaOfBygoneErasMapImageBlockWidth,
        splitRow: GameMapRows * 2,
        splitCol: GameMapCols * 2)
    {
        Layers = BaseMapLayer.LoadLayers(this);
    }

}