using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using OpenCvSharp;
using System;
using System.Diagnostics;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class EntireMap : Singleton<EntireMap>
{
    // 这个模板缩放大小的计算方式 https://github.com/babalae/better-genshin-impact/issues/318
    public static readonly Size TemplateSize = new(240, 135);

    // 对无用部分进行裁剪（左160，上80，下96）
    public static readonly Rect TemplateSizeRoi = new(20, 10, TemplateSize.Width - 20, TemplateSize.Height - 22);

    private readonly FeatureMatcher _featureMatcher;

    private float _prevX = -1;
    private float _prevY = -1;

    public EntireMap()
    {
        _featureMatcher = new FeatureMatcher(new Size(MapCoordinate.Main2048Width, MapCoordinate.Main2048Height), new FeatureStorage("mainMap2048Block"));
    }

    public FeatureMatcher GetFeatureMatcher()
    {
        return _featureMatcher;
    }

    private int _failCnt = 0;

    public void SetPrevPosition(float x, float y)
    {
        (_prevX, _prevY) = (x, y);
    }

    /// <summary>
    /// 基于特征匹配获取地图位置
    /// 移动匹配
    /// </summary>
    /// <param name="greyMat">灰度图</param>
    /// <param name="mask">遮罩</param>
    /// <returns></returns>
    public Point2f GetMiniMapPositionByFeatureMatch(Mat greyMat, Mat? mask = null)
    {
        try
        {
            Point2f p;
            if (_prevX <= 0 && _prevY <= 0)
            {
                p = _featureMatcher.KnnMatch(greyMat, mask);
            }
            else
            {
                p = _featureMatcher.KnnMatch(greyMat, _prevX, _prevY, mask, DescriptorMatcherType.BruteForce);
            }

            if (p.IsEmpty())
            {
                throw new InvalidOperationException();
            }
            _prevX = p.X;
            _prevY = p.Y;
            _failCnt = 0;
            return p;
        }
        catch
        {
            Debug.WriteLine("Feature Match Failed");
            _failCnt++;
            if (_failCnt > 5)
            {
                Debug.WriteLine("Feature Match Failed Too Many Times, 重新从全地图进行特征匹配");
                _failCnt = 0;
                (_prevX, _prevY) = (-1, -1);
            }
            return new Point2f();
        }
    }

    /// <summary>
    /// 基于特征匹配获取地图位置 全部匹配
    /// </summary>
    /// <param name="greyMat"></param>
    /// <returns></returns>
    public Point2f GetBigMapPositionByFeatureMatch(Mat greyMat)
    {
        try
        {
            var p = _featureMatcher.Match(greyMat);
            if (p.IsEmpty())
            {
                throw new InvalidOperationException();
            }
            return p;
        }
        catch
        {
            Debug.WriteLine("Feature Match Failed");
            return new Point2f();
        }
    }
}
