using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 远古圣山
/// 地图大小从4x4改到了2x2
/// </summary>
public class AncientSacredMountainMap : SceneBaseMap
{
    #region 地图参数

    static readonly int GameMapRows = 4; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 4; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 1; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    static readonly int GameMapLeftCols = 1; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 地图参数

    static readonly int AncientSacredMountainMapImageBlockWidth = 1024;

    public AncientSacredMountainMap() : base(type: MapTypes.AncientSacredMountain,
        mapSize: new Size(GameMapCols * AncientSacredMountainMapImageBlockWidth, GameMapRows * AncientSacredMountainMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * AncientSacredMountainMapImageBlockWidth, (GameMapUpRows + 1) * AncientSacredMountainMapImageBlockWidth),
        mapImageBlockWidth: AncientSacredMountainMapImageBlockWidth,
        splitRow: 0,
        splitCol: 0)
    {
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/AncientSacredMountain/AncientSacredMountain_0_1024.png"));
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/AncientSacredMountain/AncientSacredMountain_-1_1024.webp"));
        Layers = BaseMapLayer.LoadLayers(this);
    }

}