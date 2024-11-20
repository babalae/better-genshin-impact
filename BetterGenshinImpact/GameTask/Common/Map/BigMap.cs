using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Model;
using OpenCvSharp;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.Common.Map;

/// <summary>
/// 专门用于大地图的识别
/// 图像缩小了8倍
/// </summary>
public class BigMap : Singleton<BigMap>
{
    // 直接从图像加载特征点
    private readonly FeatureMatcher _featureMatcher = new(new Size(MapCoordinate.Main256Width, MapCoordinate.Main256Height), new FeatureStorage("mainMap256Block"));

    // 相对标准1024区块的缩放比例
    public const int ScaleTo1024 = 4;

    // 相对2048区块的缩放比例
    public const int ScaleTo2048 = ScaleTo1024 * 2;

    /// <summary>
    /// 基于特征匹配获取地图位置 全部匹配
    /// </summary>
    /// <param name="greyMat">传入的大地图图像会缩小8倍</param>
    /// <returns></returns>
    public Point2f GetBigMapPositionByFeatureMatch(Mat greyMat)
    {
        try
        {
            greyMat = ResizeHelper.Resize(greyMat, 1d / 4);

            return _featureMatcher.Match(greyMat);
        }
        catch
        {
            Debug.WriteLine("Feature Match Failed");
            return new Point2f();
        }
    }

    public Rect GetBigMapRectByFeatureMatch(Mat greyMat)
    {
        try
        {
            greyMat = ResizeHelper.Resize(greyMat, 1d / 4);

            return _featureMatcher.KnnMatchRect(greyMat);
        }
        catch
        {
            Debug.WriteLine("Feature Match Failed");
            return Rect.Empty;
        }
    }
}
