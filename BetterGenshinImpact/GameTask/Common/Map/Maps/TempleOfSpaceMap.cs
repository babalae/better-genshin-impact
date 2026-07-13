using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 空之神殿
/// </summary>
public class TempleOfSpaceMap : SceneBaseMap
{
    #region 每次地图扩大都要更新的参数

    static readonly int GameMapRows = 4; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 3; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 1; // 游戏坐标下 左上角离地图原点的行数(注意原点在0_0块的右下角)
    static readonly int GameMapLeftCols = 1; // 游戏坐标下 左上角离地图原点的列数(注意原点在0_0块的右下角)

    #endregion 每次地图扩大都要更新的参数

    static readonly int TempleOfSpaceMapImageBlockWidth = 1024;

    public TempleOfSpaceMap() : base(type: MapTypes.TempleOfSpace,
        mapSize: new Size(GameMapCols * TempleOfSpaceMapImageBlockWidth, GameMapRows * TempleOfSpaceMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * TempleOfSpaceMapImageBlockWidth, (GameMapUpRows + 1) * TempleOfSpaceMapImageBlockWidth),
        mapImageBlockWidth: TempleOfSpaceMapImageBlockWidth,
        splitRow: 0,
        splitCol: 0)
    {
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/TempleOfSpace/TempleOfSpace_0_1024.png"));
    }

}