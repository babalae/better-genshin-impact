using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 霜月
/// </summary>
public class MoonMap : SceneBaseMap
{
    #region 每次地图扩大都要更新的参数

    static readonly int GameMapRows = 9; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 17; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 3; // 游戏坐标下 左上角离地图原点的行数(注意原点在0_0块的右下角)
    static readonly int GameMapLeftCols = 10; // 游戏坐标下 左上角离地图原点的列数(注意原点在0_0块的右下角)

    #endregion 每次地图扩大都要更新的参数

    static readonly int MoonMapImageBlockWidth = 1024;

    public MoonMap() : base(type: MapTypes.MoonCanon,
        mapSize: new Size(GameMapCols * MoonMapImageBlockWidth, GameMapRows * MoonMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * MoonMapImageBlockWidth, (GameMapUpRows + 1) * MoonMapImageBlockWidth),
        mapImageBlockWidth: MoonMapImageBlockWidth,
        splitRow: 0,
        splitCol: 0)
    {
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/MoonCanon/MoonCanon_0_1024.png"));
    }

}
