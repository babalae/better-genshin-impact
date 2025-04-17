using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Documents;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class EntireMap : Singleton<EntireMap>
{
    // 这个模板缩放大小的计算方式 https://github.com/babalae/better-genshin-impact/issues/318
    public static readonly Size TemplateSize = new(240, 135);

    // 对无用部分进行裁剪（左160，上80，下96）
    public static readonly Rect TemplateSizeRoi = new(20, 10, TemplateSize.Width - 20, TemplateSize.Height - 22);

    private List<FeatureMatcher> _featureMatchers = new();

    private List<string> _kpList = new()
    {
        "mainMap2048Block",
        // 在这里添加所有需要加载的map
    };

    private float _prevX = -1;
    private float _prevY = -1;
    private int _prevFloor = -1;

    public EntireMap()
    {
        foreach (var curr in _kpList)
        {
            _featureMatchers.Add(new FeatureMatcher(new Size(MapCoordinate.Main2048Width, MapCoordinate.Main2048Height), new FeatureStorage(curr)));
        }
    }

    public FeatureMatcher GetFeatureMatcher()
    {
        return _featureMatchers[0];
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
    /// <param name="returnAsPoint3f">如想使用这个Point3f重载，则必须为true，若想使用Point2f重载，则不填写这项参数</param>
    /// <param name="mask">遮罩</param>
    /// <returns></returns>
    public Point3f GetMiniMapPositionByFeatureMatch(Mat greyMat, bool returnAsPoint3f, Mat? mask = null)
    {
        if (!returnAsPoint3f)
        {
            // 使用point3f重载却不让返回point3f，自相矛盾
            throw new InvalidOperationException();
        }
        try
        {
            Point2f p = new Point2f();
            
            int currFloor = 0;
            if (_prevFloor >= 0)
            {
                currFloor = _prevFloor;
            }
            while (true)
            {
                var featureMatcher = _featureMatchers[currFloor];
                if (_prevX <= 0 && _prevY <= 0)
                {
                    p = featureMatcher.KnnMatch(greyMat, mask);
                }
                else
                {
                    p = featureMatcher.KnnMatch(greyMat, _prevX, _prevY, mask, DescriptorMatcherType.BruteForce);
                }

                if (!p.IsEmpty())
                {
                    break;
                }
                currFloor++;
                if (currFloor >= _kpList.Count)
                {
                    throw new InvalidOperationException();
                }
            }
            
            _prevX = p.X;
            _prevY = p.Y;
            _prevFloor = currFloor;
            _failCnt = 0;
            return new Point3f(_prevX, _prevY, currFloor);
        }
        catch
        {
            Debug.WriteLine("Feature Match Failed");
            _failCnt++;
            if (_failCnt > 5)
            {
                Debug.WriteLine("Feature Match Failed Too Many Times, 重新从全地图进行特征匹配");
                _failCnt = 0;
                (_prevX, _prevY, _prevFloor) = (-1, -1, -1);
            }
            return new Point3f();
        }
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
        Point3f p = GetMiniMapPositionByFeatureMatch(greyMat, true, mask);
        return new Point2f(p.X, p.Y);
    }

    /// <summary>
    /// 基于特征匹配获取地图位置 全部匹配
    /// </summary>
    /// <param name="greyMat"></param>
    /// <returns></returns>
    public Point3f GetBigMapPositionByFeatureMatch(Mat greyMat, bool returnAsPoint3f, Mat? mask = null)
    {
        if (!returnAsPoint3f)
        {
            // 使用point3f重载却不让返回point3f，自相矛盾
            throw new InvalidOperationException();
        }
        try
        {
            Point2f p = new Point2f();
            int currFloor = 0;
            foreach (var featureMatcher in _featureMatchers)
            {
                p = featureMatcher.Match(greyMat, mask);
                if (!p.IsEmpty())
                {
                    break;
                }
                currFloor++;
            }
            
            if (currFloor > _kpList.Count)
            {
                throw new InvalidOperationException();
            }
            _prevX = p.X;
            _prevY = p.Y;
            _prevFloor = currFloor;
            return new Point3f(_prevX, _prevY, currFloor);
        }
        catch
        {
            Debug.WriteLine("Feature Match Failed");
            return new Point3f();
        }
    }
}
