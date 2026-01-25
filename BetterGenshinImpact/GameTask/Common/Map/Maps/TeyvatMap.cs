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
public class TeyvatMap : SceneBaseMap
{
    #region 每次地图扩大都要更新的参数

    public static readonly int GameMapRows = 15; // 游戏坐标下地图块的行数
    public static readonly int GameMapCols = 22; // 游戏坐标下地图块的列数
    public static readonly int GameMapUpRows = 7; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    public static readonly int GameMapLeftCols = 15; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 每次地图扩大都要更新的参数


    public static readonly int TeyvatMapImageBlockWidth = 2048;

    public TeyvatMap() : base(type: MapTypes.Teyvat,
        mapSize: new Size(GameMapCols * TeyvatMapImageBlockWidth, GameMapRows * TeyvatMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * TeyvatMapImageBlockWidth, (GameMapUpRows + 1) * TeyvatMapImageBlockWidth),
        mapImageBlockWidth: TeyvatMapImageBlockWidth,
        splitRow: GameMapRows * 2,
        splitCol: GameMapCols * 2)
    {
    }
    

    // 大地图使用256  相对 2048 区块的缩放比例  2048/256=8
    public const int BigMap256ScaleTo2048 = 8;

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

    public Rect GetBigMapRect(Mat greyBigMapMat, Rect prevRect)
    {
        return BigMapTeyvat256Layer.GetInstance(this).GetBigMapRect(greyBigMapMat, prevRect);
    }
}
