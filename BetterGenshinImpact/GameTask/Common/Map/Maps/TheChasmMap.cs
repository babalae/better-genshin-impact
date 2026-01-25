using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 层岩巨渊
/// 地图大小从3x3改到了2x2
/// </summary>
public class TheChasmMap : SceneBaseMap
{
    #region 每次地图扩大都要更新的参数(层岩巨渊无需更新)

    static readonly int GameMapRows = 2; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 2; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 1; // 游戏坐标下 左上角离地图原点的行数(注意原点在0_0块的右下角)
    static readonly int GameMapLeftCols = 1; // 游戏坐标下 左上角离地图原点的列数(注意原点在0_0块的右下角)

    #endregion 每次地图扩大都要更新的参数(层岩巨渊无需更新)

    static readonly int TheChasmMapImageBlockWidth = 1024;

    public TheChasmMap() : base(type: MapTypes.TheChasm,
        mapSize: new Size(GameMapCols * TheChasmMapImageBlockWidth, GameMapRows * TheChasmMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * TheChasmMapImageBlockWidth, (GameMapUpRows + 1) * TheChasmMapImageBlockWidth),
        mapImageBlockWidth: TheChasmMapImageBlockWidth,
        splitRow: 0,
        splitCol: 0)
    {
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/TheChasm/TheChasm_0_1024.png"));
    }

}