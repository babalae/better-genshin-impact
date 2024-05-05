using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using OpenCvSharp.XFeatures2D;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common.Map;
using OpenCvSharp.Features2D;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

public class FeatureMatcher
{
    private readonly double _threshold = 100; // SURF 100
    private readonly Feature2D _feature2D;
    private readonly Mat _trainMat; // 大图本身
    private readonly Mat _trainRet = new(); // 大图特征描述子
    private readonly KeyPoint[] _trainKeyPoints;

    private readonly KeyPointFeatureBlock[][] _blocks; // 特征块存储
    private readonly int _splitRow = MapCoordinate.GameMapRows * 2; // 特征点拆分行数
    private readonly int _splitCol = MapCoordinate.GameMapCols * 2; // 特征点拆分列数
    private KeyPointFeatureBlock? _lastMergedBlock; // 上次合并的特征块

    public FeatureMatcher(Mat trainMat, FeatureStorage? featureStorage = null, Feature2DType type = Feature2DType.SIFT, double threshold = 100)
    {
        _threshold = threshold;
        _trainMat = trainMat;
        if (Feature2DType.SURF == type)
        {
            _feature2D = SURF.Create(_threshold, 4, 3, false, true);
        }
        else
        {
            _feature2D = SIFT.Create();
        }

        if (featureStorage != null)
        {
            featureStorage.TypeName = type.ToString();
            var kpFromDisk = featureStorage.LoadKeyPointArray();
            if (kpFromDisk == null)
            {
                _feature2D.DetectAndCompute(trainMat, null, out _trainKeyPoints, _trainRet);
                featureStorage.SaveKeyPointArray(_trainKeyPoints);
                featureStorage.SaveDescMat(_trainRet);
            }
            else
            {
                _trainKeyPoints = kpFromDisk;
                _trainRet = featureStorage.LoadDescMat() ?? throw new Exception("加载特征描述矩阵失败");
            }
        }

        Debug.WriteLine("被匹配的图像生成初始化KeyPoint完成");
        Stopwatch sw = new();
        sw.Start();
        _blocks = KeyPointFeatureBlockHelper.SplitFeatures(_trainMat, _splitRow, _splitCol, _trainKeyPoints, _trainRet);
        sw.Stop();
        Debug.WriteLine($"切割特征点耗时: {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 普通匹配（全图特征）
    /// </summary>
    /// <param name="queryMat"></param>
    /// <returns></returns>
    public Point2f[]? Match(Mat queryMat)
    {
        return Match(_trainKeyPoints, _trainRet, queryMat);
    }

    /// <summary>
    /// 合并邻近的特征点后匹配（临近特征）
    /// </summary>
    /// <param name="queryMat"></param>
    /// <param name="prevX">上次匹配到的坐标x</param>
    /// <param name="prevY">上次匹配到的坐标y</param>
    /// <returns></returns>
    public Point2f[]? Match(Mat queryMat, int prevX, int prevY)
    {
        var (cellRow, cellCol) = KeyPointFeatureBlockHelper.GetCellIndex(_trainMat, _splitRow, _splitCol, prevX, prevY);
        if (_lastMergedBlock == null || _lastMergedBlock.MergedCenterCellRow != cellRow || _lastMergedBlock.MergedCenterCellCol != cellCol)
        {
            _lastMergedBlock = KeyPointFeatureBlockHelper.MergeNeighboringFeatures(_blocks, _trainRet, cellRow, cellCol);
        }

        return Match(_lastMergedBlock.KeyPointArray, _lastMergedBlock.Descriptor!, queryMat);
    }

    /// <summary>
    /// 普通匹配
    /// </summary>
    /// <param name="trainKeyPoints"></param>
    /// <param name="trainRet"></param>
    /// <param name="queryMat"></param>
    /// <returns></returns>
    public Point2f[]? Match(KeyPoint[] trainKeyPoints, Mat trainRet, Mat queryMat)
    {
        SpeedTimer speedTimer = new();

        using var queryRet = new Mat();

        _feature2D.DetectAndCompute(queryMat, null, out var queryKeyPoints, queryRet);
        speedTimer.Record("模板生成KeyPoint");

        using var flnMatcher = new FlannBasedMatcher();
        var matches = flnMatcher.Match(queryRet, trainRet);
        //Finding the Minimum and Maximum Distance
        double minDistance = 1000; //Backward approximation
        double maxDistance = 0;
        for (int i = 0; i < queryRet.Rows; i++)
        {
            double distance = matches[i].Distance;
            if (distance > maxDistance)
            {
                maxDistance = distance;
            }

            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }

        Debug.WriteLine($"max distance : {maxDistance}");
        Debug.WriteLine($"min distance : {minDistance}");

        var pointsQuery = new List<Point2f>();
        var pointsTrain = new List<Point2f>();
        //Screening better matching points
        // var goodMatches = new List<DMatch>();
        for (int i = 0; i < queryRet.Rows; i++)
        {
            double distance = matches[i].Distance;
            if (distance < Math.Max(minDistance * 2, 0.02))
            {
                pointsQuery.Add(queryKeyPoints[matches[i].QueryIdx].Pt);
                pointsTrain.Add(trainKeyPoints[matches[i].TrainIdx].Pt);
                //Compression of new ones with distances less than ranges DMatch
                // goodMatches.Add(matches[i]);
            }
        }

        speedTimer.Record("FlannMatch");

        // var outMat = new Mat();

        // algorithm RANSAC Filter the matched results
        var pQuery = pointsQuery.ToPoint2d();
        var pTrain = pointsTrain.ToPoint2d();
        var outMask = new Mat();
        // If the original matching result is null, Skip the filtering step
        if (pQuery.Count > 0 && pTrain.Count > 0)
        {
            var hMat = Cv2.FindHomography(pQuery, pTrain, HomographyMethods.Ransac, mask: outMask);
            speedTimer.Record("FindHomography");

            var objCorners = new Point2f[4];
            objCorners[0] = new Point2f(0, 0);
            objCorners[1] = new Point2f(0, queryMat.Rows);
            objCorners[2] = new Point2f(queryMat.Cols, queryMat.Rows);
            objCorners[3] = new Point2f(queryMat.Cols, 0);

            var sceneCorners = Cv2.PerspectiveTransform(objCorners, hMat);
            speedTimer.Record("PerspectiveTransform");
            speedTimer.DebugPrint();
            return sceneCorners;
        }

        speedTimer.DebugPrint();
        return null;
    }

    /// <summary>
    /// knn的方式匹配
    /// </summary>
    /// <param name="matSrc"></param>
    /// <returns></returns>
    [Obsolete]
    public Point2f[]? KnnMatch(Mat matSrc)
    {
        SpeedTimer speedTimer = new();

        using var matSrcRet = new Mat();
        KeyPoint[] keyPointsSrc;
        using (var surf = SURF.Create(_threshold, 4, 3, false, true))
        {
            surf.DetectAndCompute(matSrc, null, out keyPointsSrc, matSrcRet);
            speedTimer.Record("模板生成KeyPoint");
        }

        using var flnMatcher = new FlannBasedMatcher();
        var knnMatches = flnMatcher.KnnMatch(matSrcRet, _trainRet, 2);
        var minRatio = 0.66;

        var pointsSrc = new List<Point2f>();
        var pointsDst = new List<Point2f>();
        //Screening better matching points
        // var goodMatches = new List<DMatch>();
        for (int i = 0; i < knnMatches.Length; i++)
        {
            var best = knnMatches[i][0];
            var better = knnMatches[i][1];
            var r = best.Distance / better.Distance;
            if (r > minRatio)
            {
                pointsSrc.Add(keyPointsSrc[best.QueryIdx].Pt);
                pointsDst.Add(_trainKeyPoints[best.TrainIdx].Pt);
            }
        }

        speedTimer.Record("FlannMatch.KnnMatch");

        // var outMat = new Mat();

        // algorithm RANSAC Filter the matched results
        var pSrc = pointsSrc.ToPoint2d();
        var pDst = pointsDst.ToPoint2d();
        var outMask = new Mat();
        // If the original matching result is null, Skip the filtering step
        if (pSrc.Count > 0 && pDst.Count > 0)
        {
            var hMat = Cv2.FindHomography(pSrc, pDst, HomographyMethods.Ransac, mask: outMask);
            speedTimer.Record("FindHomography");

            var objCorners = new Point2f[4];
            objCorners[0] = new Point2f(0, 0);
            objCorners[1] = new Point2f(0, matSrc.Rows);
            objCorners[2] = new Point2f(matSrc.Cols, matSrc.Rows);
            objCorners[3] = new Point2f(matSrc.Cols, 0);

            var sceneCorners = Cv2.PerspectiveTransform(objCorners, hMat);
            speedTimer.Record("PerspectiveTransform");
            speedTimer.DebugPrint();
            return sceneCorners;
        }

        speedTimer.DebugPrint();
        return null;
    }
}
