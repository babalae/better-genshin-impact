using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Layer;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 提瓦特大陆
/// </summary>
public class TeyvatMapTest : SceneBaseMapByTemplateMatch
{
    #region 每次地图扩大都要更新的参数

    static readonly int GameMapRows = TeyvatMap.GameMapRows; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = TeyvatMap.GameMapCols; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = TeyvatMap.GameMapUpRows; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    static readonly int GameMapLeftCols = TeyvatMap.GameMapLeftCols; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 每次地图扩大都要更新的参数
    static readonly int TeyvatMapImageBlockWidth = TeyvatMap.TeyvatMapImageBlockWidth;

    public TeyvatMapTest() : base(type: MapTypes.Teyvat,
        mapSize: new Size(GameMapCols * TeyvatMapImageBlockWidth, GameMapRows * TeyvatMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * TeyvatMapImageBlockWidth, (GameMapUpRows + 1) * TeyvatMapImageBlockWidth),
        mapImageBlockWidth: TeyvatMapImageBlockWidth,
        splitRow: GameMapRows * 2,
        splitCol: GameMapCols * 2)
    {
    }

    public override Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        greyBigMapMat = ResizeHelper.Resize(greyBigMapMat, 1d / 4);
        var layer = BigMapTeyvat256Layer.GetInstance(this);
        return SiftMatcher.Match(layer.TrainKeyPoints, layer.TrainDescriptors, greyBigMapMat);
    }

    public override Rect GetBigMapRect(Mat greyBigMapMat)
    {
        return BigMapTeyvat256Layer.GetInstance(this).GetBigMapRect(greyBigMapMat);
    }
}
