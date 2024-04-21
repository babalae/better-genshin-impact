using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using OpenCvSharp.XFeatures2D;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public class SurfMatcher
{
    private readonly double _threshold;
    private readonly SURF _surf;
    private readonly Mat _trainMat; // 大图本身
    private readonly Mat _trainRet = new(); // 大图特征描述子
    private readonly KeyPoint[] _trainKeyPoints;

    private readonly KeyPointFeatureBlock[][] _blocks;
    private readonly int _splitRow = 13 * 2;
    private readonly int _splitCol = 14 * 2;
    private KeyPointFeatureBlock? _lastMergedBlock;

    public SurfMatcher(Mat trainMat, double threshold = 100)
    {
        _threshold = threshold;
        _trainMat = trainMat;
        _surf = SURF.Create(_threshold, 4, 3, false, true);
        _surf.DetectAndCompute(trainMat, null, out _trainKeyPoints, _trainRet);
        Debug.WriteLine("被匹配的图像生成初始化KeyPoint完成");
        _blocks = SplitFeatures(_trainMat, _splitRow, _splitCol, _trainKeyPoints, _trainRet);
        Debug.WriteLine("切割特征点完成");
    }

    public static KeyPointFeatureBlock[][] SplitFeatures(Mat originalImage, int rows, int cols, KeyPoint[] keyPoints, Mat matches)
    {
        // Calculate grid size
        int cellWidth = originalImage.Width / cols;
        int cellHeight = originalImage.Height / rows;

        // Initialize arrays to store split features and matches
        var splitKeyPoints = new KeyPointFeatureBlock[rows][];

        // Initialize each row
        for (int i = 0; i < rows; i++)
        {
            splitKeyPoints[i] = new KeyPointFeatureBlock[cols];
        }

        // Split features and matches
        for (int i = 0; i < keyPoints.Length; i++)
        {
            int row = (int)(keyPoints[i].Pt.Y / cellHeight);
            int col = (int)(keyPoints[i].Pt.X / cellWidth);

            // Ensure row and col are within bounds
            row = Math.Min(Math.Max(row, 0), rows - 1);
            col = Math.Min(Math.Max(col, 0), cols - 1);

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (splitKeyPoints[row][col] == null)
            {
                splitKeyPoints[row][col] = new KeyPointFeatureBlock();
            }

            splitKeyPoints[row][col].KeyPointList.Add(keyPoints[i]);
            splitKeyPoints[row][col].KeyPointIndexList.Add(i);
        }

        // 遍历每个特征块，计算描述子
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (splitKeyPoints[i][j] == null)
                {
                    continue;
                }

                var block = splitKeyPoints[i][j];

                var descriptor = new Mat(block.KeyPointIndexList.Count, 64, MatType.CV_32FC1);
                InitBlockMat(block.KeyPointIndexList, descriptor, matches);

                block.Descriptor = descriptor;
            }
        }

        return splitKeyPoints;
    }

    public static (int, int) GetCellIndex(Mat originalImage, int rows, int cols, int x, int y)
    {
        // Calculate grid size
        int cellWidth = originalImage.Width / cols;
        int cellHeight = originalImage.Height / rows;

        // Calculate cell index for the given point
        int cellRow = (int)(y / cellHeight);
        int cellCol = (int)(x / cellWidth);

        return (cellRow, cellCol);
    }

    public static KeyPointFeatureBlock MergeNeighboringFeatures(KeyPointFeatureBlock[][] splitKeyPoints, Mat matches, int cellRow, int cellCol)
    {
        // Initialize lists to store neighboring features and matches
        var neighboringKeyPoints = new List<KeyPoint>();
        var neighboringKeyPointIndices = new List<int>();

        // Loop through 9 neighboring cells
        for (int i = Math.Max(cellRow - 1, 0); i <= Math.Min(cellRow + 1, splitKeyPoints.Length - 1); i++)
        {
            for (int j = Math.Max(cellCol - 1, 0); j <= Math.Min(cellCol + 1, splitKeyPoints[i].Length - 1); j++)
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (splitKeyPoints[i][j] != null)
                {
                    neighboringKeyPoints.AddRange(splitKeyPoints[i][j].KeyPointList);
                    neighboringKeyPointIndices.AddRange(splitKeyPoints[i][j].KeyPointIndexList);
                }
            }
        }

        // Merge neighboring features
        var mergedKeyPointBlock = new KeyPointFeatureBlock
        {
            MergedCenterCellCol = cellCol,
            MergedCenterCellRow = cellRow,
            KeyPointList = neighboringKeyPoints,
            KeyPointIndexList = neighboringKeyPointIndices
        };
        mergedKeyPointBlock.Descriptor = new Mat(mergedKeyPointBlock.KeyPointIndexList.Count, 64, MatType.CV_32FC1);
        InitBlockMat(mergedKeyPointBlock.KeyPointIndexList, mergedKeyPointBlock.Descriptor, matches);

        return mergedKeyPointBlock;
    }

    /// <summary>
    /// 按行拷贝matches到descriptor
    /// </summary>
    /// <param name="keyPointIndexList">记录了哪些行需要拷贝</param>
    /// <param name="descriptor">拷贝结果</param>
    /// <param name="matches">原始数据</param>
    private static unsafe void InitBlockMat(IReadOnlyList<int> keyPointIndexList, Mat descriptor, Mat matches)
    {
        int cols = matches.Cols;
        float* ptrDest = (float*)descriptor.DataPointer;

        for (int i = 0; i < keyPointIndexList.Count; i++)
        {
            var index = keyPointIndexList[i];
            float* ptrSrcRow = (float*)matches.Ptr(index);
            for (int j = 0; j < cols; j++)
            {
                *(ptrDest + i * cols + j) = ptrSrcRow[j];
            }
        }
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
        var (cellRow, cellCol) = GetCellIndex(_trainMat, _splitRow, _splitCol, prevX, prevY);
        if (_lastMergedBlock == null || _lastMergedBlock.MergedCenterCellRow != cellRow || _lastMergedBlock.MergedCenterCellCol != cellCol)
        {
            _lastMergedBlock = MergeNeighboringFeatures(_blocks, _trainRet, cellRow, cellCol);
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
