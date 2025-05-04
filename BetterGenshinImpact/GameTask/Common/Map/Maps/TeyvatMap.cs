using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 提瓦特大陆
/// </summary>
public class TeyvatMap : SceneBaseMap
{
    #region 每次地图扩大都要更新的参数

    static readonly int GameMapRows = 13; // 游戏坐标下地图块的行数
    static readonly int GameMapCols = 18; // 游戏坐标下地图块的列数
    static readonly int GameMapUpRows = 5; // 游戏坐标下 左上角离地图原点的行数(注意原点在块的右下角)
    static readonly int GameMapLeftCols = 11; // 游戏坐标下 左上角离地图原点的列数(注意原点在块的右下角)

    #endregion 每次地图扩大都要更新的参数


    static readonly int TeyvatMapImageBlockWidth = 2048;

    private readonly BaseMapLayer _teyvat256MapLayer;

    public TeyvatMap() : base(type: MapTypes.Teyvat,
        mapSize: new Size(GameMapCols * TeyvatMapImageBlockWidth, GameMapRows * TeyvatMapImageBlockWidth),
        mapOriginInImageCoordinate: new Point2f((GameMapLeftCols + 1) * TeyvatMapImageBlockWidth, (GameMapUpRows + 1) * TeyvatMapImageBlockWidth),
        mapImageBlockWidth: TeyvatMapImageBlockWidth,
        splitRow: GameMapRows * 2,
        splitCol: GameMapCols * 2)
    {
        TaskControl.Logger.LogInformation("提瓦特大陆地图特征点加载中，预计耗时2秒，请等待...");
        
        Layers = BaseMapLayer.LoadLayers(this);
        var layerDir = Path.Combine(Global.Absolute(@"Assets\Map\"), Type.ToString());

        // 256用于大地图匹配
        _teyvat256MapLayer = BaseMapLayer.LoadLayer(this, Path.Combine(layerDir, "Teyvat_0_256_SIFT.kp.bin"), Path.Combine(layerDir, "Teyvat_0_256_SIFT.mat.png"));
        TaskControl.Logger.LogInformation("地图特征点加载完成！");

    }
    

    // 大地图使用256  相对 2048 区块的缩放比例  2048/256=8
    public const int BigMap256ScaleTo2048 = 8;

    public override Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        greyBigMapMat = ResizeHelper.Resize(greyBigMapMat, 1d / 4);
        return SiftMatcher.Match(_teyvat256MapLayer.TrainKeyPoints, _teyvat256MapLayer.TrainDescriptors, greyBigMapMat);
    }

    public override Rect GetBigMapRect(Mat greyBigMapMat)
    {
        greyBigMapMat = ResizeHelper.Resize(greyBigMapMat, 1d / 4);
        return SiftMatcher.KnnMatchRect(_teyvat256MapLayer.TrainKeyPoints, _teyvat256MapLayer.TrainDescriptors, greyBigMapMat);
    }
}