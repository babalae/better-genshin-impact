using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using OpenCvSharp.XFeatures2D;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

[Obsolete]
public class FeatureMatcher
{
    private readonly double _threshold = 100; // SURF 100
    private readonly Feature2D _feature2D;

    private readonly Dictionary<DescriptorMatcherType, DescriptorMatcher> _matcherFactory = new()
    {
        { DescriptorMatcherType.BruteForce, DescriptorMatcher.Create(DescriptorMatcherType.BruteForce.ToString()) },
        { DescriptorMatcherType.FlannBased, DescriptorMatcher.Create(DescriptorMatcherType.FlannBased.ToString()) }
    };

    private readonly Size _trainMatSize; // 大图大小

    private readonly Mat _trainDescriptors = new(); // 大图特征描述子
    private readonly KeyPoint[] _trainKeyPoints;

    private readonly KeyPointFeatureBlock[][] _blocks; // 特征块存储
    private readonly int _splitRow = TeyvatMapCoordinate.GameMapRows * 2; // 特征点拆分行数
    private readonly int _splitCol = TeyvatMapCoordinate.GameMapCols * 2; // 特征点拆分列数
    private KeyPointFeatureBlock? _lastMergedBlock; // 上次合并的特征块

    /// <summary>
    /// 从图像 or 特征点加载
    /// 大图不建议使用此构造函数加载，速度很慢
    /// </summary>
    /// <param name="trainMat"></param>
    /// <param name="featureStorage"></param>
    /// <param name="type"></param>
    /// <exception cref="Exception"></exception>
    public FeatureMatcher(Mat trainMat, FeatureStorage? featureStorage = null, Feature2DType type = Feature2DType.SIFT)
    {
        _trainMatSize = trainMat.Size();
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
            Debug.WriteLine("尝试从磁盘加载特征点");
            var kpFromDisk = featureStorage.LoadKeyPointArray();
            if (kpFromDisk == null)
            {
                Debug.WriteLine("特征点不存在");
                _feature2D.DetectAndCompute(trainMat, null, out _trainKeyPoints, _trainDescriptors);
                featureStorage.SaveKeyPointArray(_trainKeyPoints);
                featureStorage.SaveDescMat(_trainDescriptors);
            }
            else
            {
                _trainKeyPoints = kpFromDisk;
                _trainDescriptors = featureStorage.LoadDescMat() ?? throw new Exception("加载特征描述矩阵失败");
            }
        }
        else
        {
            _feature2D.DetectAndCompute(trainMat, null, out _trainKeyPoints, _trainDescriptors);
        }

        Debug.WriteLine("被匹配的图像生成初始化KeyPoint完成");
        Stopwatch sw = new();
        sw.Start();
        _blocks = KeyPointFeatureBlockHelper.SplitFeatures(_trainMatSize, _splitRow, _splitCol, _trainKeyPoints, _trainDescriptors);
        sw.Stop();
        Debug.WriteLine($"切割特征点耗时: {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 直接从特征点加载
    /// </summary>
    /// <param name="trainMatSize"></param>
    /// <param name="featureStorage"></param>
    /// <param name="type"></param>
    /// <exception cref="Exception"></exception>
    public FeatureMatcher(Size trainMatSize, FeatureStorage featureStorage, Feature2DType type = Feature2DType.SIFT)
    {
        _trainMatSize = trainMatSize;
        if (Feature2DType.SURF == type)
        {
            _feature2D = SURF.Create(_threshold, 4, 3, false, true);
        }
        else
        {
            _feature2D = SIFT.Create();
        }

        featureStorage.TypeName = type.ToString();
        Debug.WriteLine("尝试从磁盘加载特征点");
        _trainKeyPoints = featureStorage.LoadKeyPointArray() ?? throw new Exception("特征点不存在");
        _trainDescriptors = featureStorage.LoadDescMat() ?? throw new Exception("加载特征描述矩阵失败");

        Debug.WriteLine("被匹配的图像生成初始化KeyPoint完成");
        Stopwatch sw = new();
        sw.Start();
        _blocks = KeyPointFeatureBlockHelper.SplitFeatures(_trainMatSize, _splitRow, _splitCol, _trainKeyPoints, _trainDescriptors);
        sw.Stop();
        Debug.WriteLine($"切割特征点耗时: {sw.ElapsedMilliseconds}ms");
    }

    public DescriptorMatcher GetMatcher(DescriptorMatcherType type)
    {
        return _matcherFactory[type];
    }

    #region 普通匹配

    /// <summary>
    /// 普通匹配（全图特征）
    /// </summary>
    /// <param name="queryMat"></param>
    /// <param name="queryMatMask"></param>
    /// <returns></returns>
    public Point2f Match(Mat queryMat, Mat? queryMatMask = null)
    {
        return Match(_trainKeyPoints, _trainDescriptors, queryMat, queryMatMask);
    }

    /// <summary>
    /// 合并邻近的特征点后匹配（临近特征）
    /// </summary>
    /// <param name="queryMat">查询图像的 Mat 对象。</param>
    /// <param name="prevX">上次匹配到的坐标x</param>
    /// <param name="prevY">上次匹配到的坐标y</param>
    /// <param name="queryMatMask">查询图像的 Mask，用于限定检测特征的区域。</param>
    /// <returns></returns>
    public Point2f Match(Mat queryMat, float prevX, float prevY, Mat? queryMatMask = null)
    {
        var (cellRow, cellCol) = KeyPointFeatureBlockHelper.GetCellIndex(_trainMatSize, _splitRow, _splitCol, prevX, prevY);
        Debug.WriteLine($"当前坐标({prevX},{prevY})在特征块({cellRow},{cellCol})中");
        if (_lastMergedBlock == null || _lastMergedBlock.MergedCenterCellRow != cellRow || _lastMergedBlock.MergedCenterCellCol != cellCol)
        {
            Debug.WriteLine($"---------切换到新的特征块({cellRow},{cellCol})，合并特征点--------");
            _lastMergedBlock = KeyPointFeatureBlockHelper.MergeNeighboringFeatures(_blocks, _trainDescriptors, cellRow, cellCol);
        }
        return Match(_lastMergedBlock.KeyPointArray, _lastMergedBlock.Descriptor!, queryMat, queryMatMask);
    }

    /// <summary>
    /// 用于从训练图像和查询图像中找到匹配的特征点，计算透视变换并确定查询图像的中心点在训练图像中的位置。
    /// </summary>
    /// <param name="trainKeyPoints">训练图像中的关键点集合。</param>
    /// <param name="trainDescriptors">训练图像的特征描述子。</param>
    /// <param name="queryMat">查询图像的 Mat 对象。</param>
    /// <param name="queryMatMask">查询图像的 Mask，用于限定检测特征的区域。</param>
    /// <param name="matcherType">描述符匹配器的类型，默认为 DescriptorMatcherType.FlannBased</param>
    /// <returns></returns>
    public Point2f Match(KeyPoint[] trainKeyPoints, Mat trainDescriptors, Mat queryMat, Mat? queryMatMask = null,
        DescriptorMatcherType matcherType = DescriptorMatcherType.FlannBased)
    {
        SpeedTimer speedTimer = new();

        using var queryDescriptors = new Mat();
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        _feature2D.DetectAndCompute(queryMat, queryMatMask, out var queryKeyPoints, queryDescriptors);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        speedTimer.Record("模板生成KeyPoint");

        var matches = GetMatcher(matcherType).Match(queryDescriptors, trainDescriptors);
        //Finding the Minimum and Maximum Distance
        double minDistance = 1000; //Backward approximation
        double maxDistance = 0;
        for (int i = 0; i < queryDescriptors.Rows; i++)
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

        // Debug.WriteLine($"max distance : {maxDistance}");
        // Debug.WriteLine($"min distance : {minDistance}");

        var pointsQuery = new List<Point2f>();
        var pointsTrain = new List<Point2f>();
        //Screening better matching points
        // var goodMatches = new List<DMatch>();
        for (int i = 0; i < queryDescriptors.Rows; i++)
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

            // 1. 计算查询图像的中心点
            var queryCenterPoint = new Point2f(queryMat.Cols / 2f, queryMat.Rows / 2f);

            // 2. 使用单应矩阵进行透视变换
            Point2f[] queryCenterPoints = [queryCenterPoint];
            Point2f[] transformedCenterPoints = Cv2.PerspectiveTransform(queryCenterPoints, hMat);

            // 3. 获取变换后的中心点
            var trainCenterPoint = transformedCenterPoints[0];
            speedTimer.Record("PerspectiveTransform");
            return trainCenterPoint;
        }

        speedTimer.DebugPrint();
        return new Point2f();
    }

    /// <summary>
    /// 普通匹配
    /// </summary>
    /// <param name="trainKeyPoints"></param>
    /// <param name="trainDescriptors"></param>
    /// <param name="queryMat"></param>
    /// <param name="queryMatMask"></param>
    /// <param name="matcherType"></param>
    /// <returns></returns>
    public Point2f[] MatchCorners(KeyPoint[] trainKeyPoints, Mat trainDescriptors, Mat queryMat, Mat? queryMatMask = null,
        DescriptorMatcherType matcherType = DescriptorMatcherType.FlannBased)
    {
        SpeedTimer speedTimer = new();

        using var queryDescriptors = new Mat();
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        _feature2D.DetectAndCompute(queryMat, queryMatMask, out var queryKeyPoints, queryDescriptors);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        speedTimer.Record("模板生成KeyPoint");

        var matches = GetMatcher(matcherType).Match(queryDescriptors, trainDescriptors);
        //Finding the Minimum and Maximum Distance
        double minDistance = 1000; //Backward approximation
        double maxDistance = 0;
        for (int i = 0; i < queryDescriptors.Rows; i++)
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

        // Debug.WriteLine($"max distance : {maxDistance}");
        // Debug.WriteLine($"min distance : {minDistance}");

        var pointsQuery = new List<Point2f>();
        var pointsTrain = new List<Point2f>();
        //Screening better matching points
        // var goodMatches = new List<DMatch>();
        for (int i = 0; i < queryDescriptors.Rows; i++)
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
        return [];
    }

    public Rect MatchRect(Mat queryMat, Mat? queryMatMask = null)
    {
        var corners = MatchCorners(_trainKeyPoints, _trainDescriptors, queryMat, queryMatMask);
        if (corners.Length == 0)
        {
            return default;
        }
        return Cv2.BoundingRect(corners);
    }

    #endregion 普通匹配

    #region Knn匹配

    public Point2f KnnMatch(Mat queryMat, Mat? queryMatMask = null,
        DescriptorMatcherType matcherType = DescriptorMatcherType.FlannBased)
    {
        return KnnMatch(_trainKeyPoints, _trainDescriptors, queryMat, queryMatMask, matcherType);
    }

    public Point2f KnnMatch(Mat queryMat, float prevX, float prevY, Mat? queryMatMask = null,
        DescriptorMatcherType matcherType = DescriptorMatcherType.FlannBased)
    {
        var (cellRow, cellCol) = KeyPointFeatureBlockHelper.GetCellIndex(_trainMatSize, _splitRow, _splitCol, prevX, prevY);
        Debug.WriteLine($"当前坐标({prevX},{prevY})在特征块({cellRow},{cellCol})中");
        if (_lastMergedBlock == null || _lastMergedBlock.MergedCenterCellRow != cellRow || _lastMergedBlock.MergedCenterCellCol != cellCol)
        {
            Debug.WriteLine($"---------切换到新的特征块({cellRow},{cellCol})，合并特征点--------");
            _lastMergedBlock = KeyPointFeatureBlockHelper.MergeNeighboringFeatures(_blocks, _trainDescriptors, cellRow, cellCol);
        }

        return KnnMatch(_lastMergedBlock.KeyPointArray, _lastMergedBlock.Descriptor!, queryMat, queryMatMask, matcherType);
    }

    /// <summary>
    /// https://github.com/tignioj/minimap/blob/main/matchmap/sifttest/sifttest5.py
    /// Copilot 生成
    /// </summary>
    /// <returns></returns>
    private Point2f KnnMatch(KeyPoint[] trainKeyPoints, Mat trainDescriptors, Mat queryMat, Mat? queryMatMask = null,
        DescriptorMatcherType matcherType = DescriptorMatcherType.FlannBased)
    {
        SpeedTimer speedTimer = new();
        using var queryDescriptors = new Mat();
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        _feature2D.DetectAndCompute(queryMat, queryMatMask, out var queryKeyPoints, queryDescriptors);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        speedTimer.Record("模板生成KeyPoint");
        var matches = GetMatcher(matcherType).KnnMatch(queryDescriptors, trainDescriptors, k: 2);
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
            return new Point2f();
        }

        // 获取匹配点的坐标
        var srcPts = goodMatches.Select(m => queryKeyPoints[m.QueryIdx].Pt).ToArray();
        var dstPts = goodMatches.Select(m => trainKeyPoints[m.TrainIdx].Pt).ToArray();

        speedTimer.Record("GetGoodMatchPoints");

        // 使用RANSAC找到变换矩阵
        var mask = new Mat();
        var hMat = Cv2.FindHomography(srcPts.ToList().ToPoint2d(), dstPts.ToList().ToPoint2d(), HomographyMethods.Ransac, 3.0, mask);
        if (hMat.Empty())
        {
            return new Point2f();
        }
        speedTimer.Record("FindHomography");

        // 计算小地图的中心点
        var h = queryMat.Rows;
        var w = queryMat.Cols;
        var centerPoint = new Point2f(w / 2f, h / 2f);
        Point2f[] centerPoints = [centerPoint];
        Point2f[] transformedCenter = Cv2.PerspectiveTransform(centerPoints, hMat);

        speedTimer.Record("PerspectiveTransform");
        speedTimer.DebugPrint();
        // 返回小地图在大地图中的中心坐标
        return transformedCenter[0];
    }

    public Point2f[] KnnMatchCorners(KeyPoint[] trainKeyPoints, Mat trainDescriptors, Mat queryMat, Mat? queryMatMask = null,
        DescriptorMatcherType matcherType = DescriptorMatcherType.FlannBased)
    {
        SpeedTimer speedTimer = new();
        using var queryDescriptors = new Mat();
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        _feature2D.DetectAndCompute(queryMat, queryMatMask, out var queryKeyPoints, queryDescriptors);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        speedTimer.Record("模板生成KeyPoint");
        var matches = GetMatcher(matcherType).KnnMatch(queryDescriptors, trainDescriptors, k: 2);
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
            return [];
        }

        // 获取匹配点的坐标
        var srcPts = goodMatches.Select(m => queryKeyPoints[m.QueryIdx].Pt).ToArray();
        var dstPts = goodMatches.Select(m => trainKeyPoints[m.TrainIdx].Pt).ToArray();

        speedTimer.Record("GetGoodMatchPoints");

        // 使用RANSAC找到变换矩阵
        var mask = new Mat();
        var hMat = Cv2.FindHomography(srcPts.ToList().ToPoint2d(), dstPts.ToList().ToPoint2d(), HomographyMethods.Ransac, 3.0, mask);
        if (hMat.Empty())
        {
            return [];
        }
        speedTimer.Record("FindHomography");

        // 返回四个角点
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

    public Rect KnnMatchRect(Mat queryMat, Mat? queryMatMask = null)
    {
        var corners = KnnMatchCorners(_trainKeyPoints, _trainDescriptors, queryMat, queryMatMask);
        if (corners.Length == 0)
        {
            return default;
        }
        return Cv2.BoundingRect(corners);
    }

    #endregion Knn匹配
}
