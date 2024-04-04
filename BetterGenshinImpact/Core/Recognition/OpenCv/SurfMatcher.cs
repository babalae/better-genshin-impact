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
    private readonly Mat _matToRet = new();
    private readonly KeyPoint[] _keyPointsTo;

    public SurfMatcher(Mat matTo, double threshold = 400)
    {
        _threshold = threshold;
        using var surf = SURF.Create(_threshold, 4, 3, false, true);
        surf.DetectAndCompute(matTo, null, out _keyPointsTo, _matToRet);
        Debug.WriteLine("被匹配的图像生成初始化KeyPoint完成");
    }

    public Point2f[]? Match(Mat matSrc)
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
        var matches = flnMatcher.Match(matSrcRet, _matToRet);
        //Finding the Minimum and Maximum Distance
        double minDistance = 1000; //Backward approximation
        double maxDistance = 0;
        for (int i = 0; i < matSrcRet.Rows; i++)
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

        var pointsSrc = new List<Point2f>();
        var pointsDst = new List<Point2f>();
        //Screening better matching points
        // var goodMatches = new List<DMatch>();
        for (int i = 0; i < matSrcRet.Rows; i++)
        {
            double distance = matches[i].Distance;
            if (distance < Math.Max(minDistance * 2, 0.02))
            {
                pointsSrc.Add(keyPointsSrc[matches[i].QueryIdx].Pt);
                pointsDst.Add(_keyPointsTo[matches[i].TrainIdx].Pt);
                //Compression of new ones with distances less than ranges DMatch
                // goodMatches.Add(matches[i]);
            }
        }

        speedTimer.Record("FlannMatch");

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
