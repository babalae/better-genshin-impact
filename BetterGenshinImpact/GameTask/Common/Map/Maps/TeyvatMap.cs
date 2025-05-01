using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 提瓦特大陆
/// </summary>
public class TeyvatMap : IndependentBaseMap
{
    #region 每次地图扩大都要更新的参数

    static readonly int GameMapRows = 13; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 18; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 5; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    static readonly int GameMapLeftCols = 11; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 每次地图扩大都要更新的参数


    static readonly int TeyvatMapImageBlockWidth = 2048;
    static readonly int ImageMap256Width = GameMapCols * 256;
    static readonly int ImageMap256Height = GameMapRows * 256;

    public TeyvatMap() : base(type: IndependentMapTypes.Teyvat,
        mapSize: new Size(GameMapCols * TeyvatMapImageBlockWidth, GameMapRows * TeyvatMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * TeyvatMapImageBlockWidth, (GameMapUpRows + 1) * TeyvatMapImageBlockWidth),
        mapImageBlockWidth: TeyvatMapImageBlockWidth,
        splitRow: GameMapRows * 2,
        splitCol: GameMapCols * 2)
    {
        Layers = BaseMapLayer.LoadLayers(this);
    }


    public new Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        // TODO: 跟换地图
        return SiftMatcher.Match(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public new Rect GetBigMapRect(Mat greyBigMapMat)
    {
        return SiftMatcher.KnnMatchRect(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }
}