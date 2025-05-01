using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

public class TeyvatMap() : IndependentBaseMap(name: "提瓦特大陆",
    mapSize: new Size(Main2048Width, Main2048Height),
    splitRow: GameMapRows * 2,
    splitCol: GameMapCols * 2)
{
    
    #region 每次地图扩大都要更新的参数

    public static readonly int GameMapRows = 13; // 游戏坐标下地图块的行数
    public static readonly int GameMapCols = 18; // 游戏坐标下地图块的列数
    public static readonly int GameMapUpRows = 5; // 游戏坐标下 左上角离地图原点的行数
    public static readonly int GameMapLeftCols = 11; // 游戏坐标下 左上角离地图原点的列数
    
    public static readonly int Main2048Width = GameMapCols * 2048;
    public static readonly int Main2048Height = GameMapRows * 2048;
    
    public static readonly int Main256Width = GameMapCols * 256;
    public static readonly int Main256Height = GameMapRows * 256;

    #endregion 每次地图扩大都要更新的参数
    
    public new Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        return SiftMatcher.Match(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public new Rect GetBigMapRect(Mat greyBigMapMat)
    {
        return SiftMatcher.KnnMatchRect(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }
}