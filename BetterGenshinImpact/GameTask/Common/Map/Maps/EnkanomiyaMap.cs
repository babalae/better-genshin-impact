using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 渊下宫
/// </summary>
public class EnkanomiyaMap : SceneBaseMap
{
    #region 地图参数

    static readonly int GameMapRows = 3; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 3; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 1; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    static readonly int GameMapLeftCols = 1; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 地图参数

    static readonly int EnkanomiyaMapImageBlockWidth = 1024;

    public EnkanomiyaMap() : base(type: MapTypes.Enkanomiya,
        mapSize: new Size(GameMapCols * EnkanomiyaMapImageBlockWidth, GameMapRows * EnkanomiyaMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * EnkanomiyaMapImageBlockWidth, (GameMapUpRows + 1) * EnkanomiyaMapImageBlockWidth),
        mapImageBlockWidth: EnkanomiyaMapImageBlockWidth,
        splitRow: 0,
        splitCol: 0)
    {
        ExtractAndSaveFeature(Global.Absolute("Assets/Map/Enkanomiya/Enkanomiya_0_1024.png"));
    }

}