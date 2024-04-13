using OpenCvSharp;
using OpenCvSharp.XFeatures2D;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public class SurfMatcher
{
    private readonly double _threshold;
    private readonly SURF _surf;
    private readonly Mat _trainMat = new(); // 被匹配的图像的
    private readonly KeyPoint[] _trainKeyPoints;

    public SurfMatcher(Mat trainMat, double threshold = 100)
    {
        _threshold = threshold;
        _surf = SURF.Create(_threshold, 4, 3, false, true);
        _surf.DetectAndCompute(trainMat, null, out _trainKeyPoints, _trainMat);
        Debug.WriteLine("被匹配的图像生成初始化KeyPoint完成");
    }

    /// <summary>
    /// 普通匹配
    /// </summary>
    /// <param name="queryMat"></param>
    /// <returns></returns>
    public Point2f[]? Match(Mat queryMat)
    {
        return Match(_trainMat, queryMat);
    }

    /// <summary>
    /// 普通匹配
    /// </summary>
    /// <param name="trainRet"></param>
    /// <param name="queryMat"></param>
    /// <returns></returns>
    public Point2f[]? Match(Mat trainRet, Mat queryMat)
    {
        SpeedTimer speedTimer = new();

        using var queryRet = new Mat();

        _surf.DetectAndCompute(queryMat, null, out var queryKeyPoints, queryRet);
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
                pointsTrain.Add(_trainKeyPoints[matches[i].TrainIdx].Pt);
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
        var knnMatches = flnMatcher.KnnMatch(matSrcRet, _trainMat, 2);
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
