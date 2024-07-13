using System;
using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map;

/// <summary>
/// 专门用于大地图的识别
/// 图像缩小了8倍
/// </summary>
public class BigMap : Singleton<BigMap>
{
    // 直接从图像加载特征点
    private readonly FeatureMatcher _featureMatcher = new(MapAssets.Instance.MainMap256BlockMat.Value, new FeatureStorage("mainMap256Block"));

    /// <summary>
    /// 基于特征匹配获取地图位置 全部匹配
    /// </summary>
    /// <param name="greyMat">传入的大地图图像会缩小8倍</param>
    /// <returns></returns>
    public Rect GetBigMapPositionByFeatureMatch(Mat greyMat)
    {
        try
        {
            greyMat = ResizeHelper.Resize(greyMat, 1d / 4);

            var pArray = _featureMatcher.Match(greyMat);
            if (pArray == null || pArray.Length < 4)
            {
                throw new InvalidOperationException();
            }
            return Cv2.BoundingRect(pArray);
        }
        catch
        {
            Debug.WriteLine("Feature Match Failed");
            return Rect.Empty;
        }
    }
}
