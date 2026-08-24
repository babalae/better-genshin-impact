using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

/// <summary>
/// 一次查询图特征提取的可复用结果。
/// 实例拥有 <see cref="Descriptors"/> 的生命周期，调用方应使用 <see langword="using"/> 或主动释放。
/// </summary>
/// <param name="keyPoints">从查询图提取的关键点。</param>
/// <param name="descriptors">从查询图提取的描述矩阵，其所有权转交给本实例。</param>
/// <param name="imageSize">查询图尺寸，用于透视变换后计算图像中心坐标。</param>
internal sealed class FeatureMatchQuery(KeyPoint[] keyPoints, Mat descriptors, Size imageSize) : IDisposable
{
    /// <summary>
    /// 查询图关键点。
    /// </summary>
    public KeyPoint[] KeyPoints { get; } = keyPoints;

    /// <summary>
    /// 查询图描述矩阵，由当前实例负责释放。
    /// </summary>
    public Mat Descriptors { get; } = descriptors;

    /// <summary>
    /// 查询图原始尺寸。
    /// </summary>
    public Size ImageSize { get; } = imageSize;

    /// <summary>
    /// 释放查询图描述矩阵。
    /// </summary>
    public void Dispose()
    {
        Descriptors.Dispose();
    }
}

/// <summary>
/// 支持复用查询图特征的 <see cref="Feature2D"/> 扩展。
/// 将查询图的 DetectAndCompute 与训练数据匹配拆开，使同一帧小地图只提取一次特征，
/// 随后可以按顺序与多个普通层或分组分层候选匹配。
/// </summary>
internal static class Feature2DQueryExtensions
{
    /// <summary>
    /// 复用描述匹配器以避免为每个候选层重复创建原生对象；调用匹配器时需要加锁保证线程安全。
    /// </summary>
    private static readonly Dictionary<DescriptorMatcherType, DescriptorMatcher> MatcherFactory = new()
    {
        { DescriptorMatcherType.BruteForce, DescriptorMatcher.Create(DescriptorMatcherType.BruteForce.ToString()) },
        { DescriptorMatcherType.FlannBased, DescriptorMatcher.Create(DescriptorMatcherType.FlannBased.ToString()) }
    };

    /// <summary>
    /// 提取一次查询图的关键点和描述矩阵，供后续多个训练层共同复用。
    /// </summary>
    /// <param name="feature2D">用于提取查询特征的 OpenCV 特征算法。</param>
    /// <param name="queryMat">待匹配的查询图。</param>
    /// <param name="queryMatMask">可选的查询图掩码。</param>
    /// <returns>拥有查询描述矩阵生命周期的可复用查询对象。</returns>
    internal static FeatureMatchQuery PrepareFeatureMatchQuery(this Feature2D feature2D, Mat queryMat, Mat? queryMatMask = null)
    {
        var queryDescriptors = new Mat();
        try
        {
#pragma warning disable CS8604 // 引用类型参数可能为 null。
            feature2D.DetectAndCompute(queryMat, queryMatMask, out var queryKeyPoints, queryDescriptors);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
            return new FeatureMatchQuery(queryKeyPoints, queryDescriptors, queryMat.Size());
        }
        catch
        {
            queryDescriptors.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 使用已经提取的查询特征与一组训练特征执行 KNN 匹配，并返回查询图中心在训练图坐标系中的位置。
    /// 本方法不会释放查询对象或训练数据，它们可以继续用于后续候选层。
    /// </summary>
    /// <param name="feature2D">提供扩展方法入口的特征算法实例。</param>
    /// <param name="trainKeyPoints">训练层关键点。</param>
    /// <param name="trainDescriptors">训练层描述矩阵。</param>
    /// <param name="query">已经提取且可复用的查询图特征。</param>
    /// <param name="matcherType">描述匹配器类型。</param>
    /// <returns>匹配成功时返回训练图中的中心坐标，否则返回默认坐标。</returns>
    internal static Point2f KnnMatch(
        this Feature2D feature2D,
        KeyPoint[] trainKeyPoints,
        Mat trainDescriptors,
        FeatureMatchQuery query,
        DescriptorMatcherType matcherType = DescriptorMatcherType.FlannBased)
    {
        if (trainKeyPoints.Length == 0 || trainDescriptors.Empty() || query.KeyPoints.Length == 0 || query.Descriptors.Empty())
        {
            return default;
        }

        SpeedTimer speedTimer = new();
        DMatch[][] matches;
        var matcher = MatcherFactory[matcherType];
        lock (matcher)
        {
            matches = matcher.KnnMatch(query.Descriptors, trainDescriptors, k: 2);
        }
        speedTimer.Record("FlannMatch");

        // 应用比例测试来过滤匹配点
        List<DMatch> goodMatches = [];
        foreach (var match in matches)
        {
            if (match.Length == 2 && match[0].Distance < 0.75 * match[1].Distance)
            {
                goodMatches.Add(match[0]);
            }
        }

        if (goodMatches.Count < 7)
        {
            return default;
        }

        var srcPts = goodMatches.Select(match => query.KeyPoints[match.QueryIdx].Pt).ToArray();
        var dstPts = goodMatches.Select(match => trainKeyPoints[match.TrainIdx].Pt).ToArray();
        speedTimer.Record("GetGoodMatchPoints");

        using var mask = new Mat();
        using var homography = Cv2.FindHomography(
            srcPts.ToList().ToPoint2d(),
            dstPts.ToList().ToPoint2d(),
            HomographyMethods.Ransac,
            3.0,
            mask);
        if (homography.Empty())
        {
            return default;
        }

        speedTimer.Record("FindHomography");
        var centerPoint = new Point2f(query.ImageSize.Width / 2f, query.ImageSize.Height / 2f);
        var transformedCenter = Cv2.PerspectiveTransform([centerPoint], homography);
        speedTimer.Record("PerspectiveTransform");
        speedTimer.DebugPrint();
        return transformedCenter[0];
    }
}
