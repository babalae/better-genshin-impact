using System;
using System.Diagnostics;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Layer;

public class BigMapTeyvat256Layer : BaseMapLayer
{
    // 大地图使用256  相对 2048 区块的缩放比例  2048/256=8
    public const int BigMap256ScaleTo2048 = 8;

    private static BigMapTeyvat256Layer? _instance;
    private static readonly object LockObject = new();

    private readonly Size _mapSize256;
    private readonly Feature2D _siftMatcher;
    private readonly SceneBaseMap _baseMap;

    private BigMapTeyvat256Layer(SceneBaseMap baseMap) : base(baseMap)
    {
        Floor = 0;
        var layerDir = Path.Combine(Global.Absolute(@"Assets\Map\"), baseMap.Type.ToString());
        var kpFilePath = Path.Combine(layerDir, "Teyvat_0_256_SIFT.kp.bin");
        var matFilePath = Path.Combine(layerDir, "Teyvat_0_256_SIFT.mat.png");
        TrainKeyPoints = FeatureStorageHelper.LoadKeyPointArray(kpFilePath) ?? throw new Exception($"地图数据加载失败，文件: {kpFilePath}");
        TrainDescriptors = FeatureStorageHelper.LoadDescriptorMat(matFilePath) ?? throw new Exception($"地图数据加载失败，文件: {matFilePath}");

        _mapSize256 = new Size(baseMap.MapSize.Width / BigMap256ScaleTo2048, baseMap.MapSize.Height / BigMap256ScaleTo2048);
        SplitBlocks = KeyPointFeatureBlockHelper.SplitFeatures(_mapSize256, TeyvatMap.GameMapRows * 4, TeyvatMap.GameMapCols * 4, TrainKeyPoints, TrainDescriptors);

        _siftMatcher = baseMap.SiftMatcher;
        _baseMap = baseMap;
    }

    public static BigMapTeyvat256Layer GetInstance(SceneBaseMap baseMap)
    {
        if (_instance != null)
        {
            return _instance;
        }

        lock (LockObject)
        {
            if (_instance == null)
            {
                _instance = new BigMapTeyvat256Layer(baseMap);
            }
        }

        return _instance;
    }

    public Rect GetBigMapRect(Mat greyBigMapMat)
    {
        greyBigMapMat = ResizeHelper.Resize(greyBigMapMat, 1d / 4);
        return _siftMatcher.KnnMatchRect(TrainKeyPoints, TrainDescriptors, greyBigMapMat);
    }

    public Point2f GetBigMapPosition(Mat greyBigMapMat, Point2f expectedCenter)
    {
        using var resizedGrey = ResizeHelper.Resize(greyBigMapMat, 1d / 4);
        if (!IsValidPoint(expectedCenter) || SplitBlocks.Length == 0 || SplitBlocks[0].Length == 0)
        {
            return _siftMatcher.Match(TrainKeyPoints, TrainDescriptors, resizedGrey);
        }

        var searchRect = BuildLocalSearchRect(expectedCenter, resizedGrey.Size(), _mapSize256);
        var result = _siftMatcher.KnnMatchLocal(
            SplitBlocks,
            TrainDescriptors,
            _mapSize256,
            searchRect,
            resizedGrey);
        return result == default
            ? _siftMatcher.Match(TrainKeyPoints, TrainDescriptors, resizedGrey)
            : result;
    }

    /// <summary>
    /// 根据上一个矩形位置，自适应地扩大搜索范围
    /// 自适应扩展：
    /// - blockWidth = colEnd - colStart + 1 ， blockHeight 类似，得到该矩形覆盖了多少个块。
    /// - maxBlock = max(blockWidth, blockHeight) 作为尺度。
    /// - expand = Max(1, Min(4, maxBlock / 2)) ：
    /// - 小矩形（块数少）至少扩展 1 个块；
    /// - 大矩形（块数多）根据块数扩展，但最多扩展到 4 个块，避免一下子把全图拉进来。
    /// - 在块索引上做扩展，而不是继续固定 3×3，也就实现了“根据块多少决定扩展大小”。
    /// </summary>
    /// <param name="greyBigMapMat"></param>
    /// <param name="prevRect"></param>
    /// <returns></returns>
    public Rect GetBigMapRect(Mat greyBigMapMat, Rect prevRect)
    {
        var rows = SplitBlocks.Length;
        if (prevRect == default || rows == 0)
        {
            return GetBigMapRect(greyBigMapMat);
        }

        var cols = SplitBlocks[0].Length;

        var (rowStart, rowEnd, colStart, colEnd) = KeyPointFeatureBlockHelper.GetCellRange(_mapSize256, rows, cols, prevRect);

        var blockWidth = colEnd - colStart + 1;
        var blockHeight = rowEnd - rowStart + 1;
        var maxBlock = Math.Max(blockWidth, blockHeight);

        var expand = 1;
        // var expand = Math.Max(1, Math.Min(2, maxBlock / 2));
        // Debug.WriteLine($"[提瓦特大地图]自适应扩展搜索，块范围扩展: {expand}，原范围行({rowStart},{rowEnd})列({colStart},{colEnd})");

        rowStart -= expand;
        rowEnd += expand;
        colStart -= expand;
        colEnd += expand;

        greyBigMapMat = ResizeHelper.Resize(greyBigMapMat, 1d / 4);
        var mergedBlock = KeyPointFeatureBlockHelper.MergeFeaturesInRange(SplitBlocks, TrainDescriptors, rowStart, rowEnd, colStart, colEnd);
        var keyPoints = mergedBlock.KeyPointArray;
        var descriptors = mergedBlock.Descriptor!;

        var res = _siftMatcher.KnnMatchRect(keyPoints, descriptors, greyBigMapMat);
        if (res == default)
        {
            Debug.WriteLine("[提瓦特大地图]自适应扩展搜索失败，退回全图搜索");
            return _siftMatcher.KnnMatchRect(TrainKeyPoints, TrainDescriptors, greyBigMapMat);
        }
        return res;
    }

    /// <summary>
    /// 区块限定的中心点匹配：只在先验中心附近的区块子集上做全图同款 Match，返回 256 尺度图像坐标中心点。
    /// genshinCenter：先验中心（原神坐标系）；genshinRadius：允许半径（原神坐标）。
    /// 区块内匹配失败返回 default（由调用方决定退下一层）。不含合理性校验（校验在调用层做，保持 SRP）。
    /// </summary>
    public Point2f GetBigMapPositionInRange(Mat greyBigMapMat, Point2f genshinCenter, double genshinRadius)
    {
        var rows = SplitBlocks.Length;
        if (rows == 0) return default;
        var cols = SplitBlocks[0].Length;

        // 1. 先验中心：原神坐标 → 2048 尺度图像坐标 → 256 尺度；半径同步换算
        var centerImg2048 = _baseMap.ConvertGenshinMapCoordinatesToImageCoordinates(genshinCenter);
        var cx256 = centerImg2048.X / BigMap256ScaleTo2048;
        var cy256 = centerImg2048.Y / BigMap256ScaleTo2048;
        var r256 = genshinRadius * _baseMap.MapImageBlockWidthScale / BigMap256ScaleTo2048;

        // 2. 用先验矩形取区块范围（GetCellRange 吃 256 尺度 Rect），复用 expand=1 外扩
        var priorRect = new Rect((int)(cx256 - r256), (int)(cy256 - r256), (int)(r256 * 2), (int)(r256 * 2));
        var (rowStart, rowEnd, colStart, colEnd) =
            KeyPointFeatureBlockHelper.GetCellRange(_mapSize256, rows, cols, priorRect);
        rowStart -= 1; rowEnd += 1; colStart -= 1; colEnd += 1;

        // 3. 合并该范围区块特征，只在子集上做全图同款 Match（返回 256 尺度中心点）
        var greyResized = ResizeHelper.Resize(greyBigMapMat, 1d / 4);
        var merged = KeyPointFeatureBlockHelper.MergeFeaturesInRange(
            SplitBlocks, TrainDescriptors, rowStart, rowEnd, colStart, colEnd);
        if (merged.Descriptor == null || merged.KeyPointArray.Length == 0) return default;

        return _siftMatcher.Match(merged.KeyPointArray, merged.Descriptor, greyResized);
    }

    private static bool IsValidPoint(Point2f point)
    {
        return float.IsFinite(point.X) && float.IsFinite(point.Y) &&
               (Math.Abs(point.X) > float.Epsilon || Math.Abs(point.Y) > float.Epsilon);
    }

    private static Rect BuildLocalSearchRect(Point2f center, Size querySize, Size trainImageSize)
    {
        var width = Math.Min(trainImageSize.Width, Math.Max(querySize.Width * 2.0, trainImageSize.Width / 4.0));
        var height = Math.Min(trainImageSize.Height, Math.Max(querySize.Height * 2.0, trainImageSize.Height / 4.0));
        var rectWidth = (int)Math.Round(width);
        var rectHeight = (int)Math.Round(height);
        var x = Math.Clamp((int)Math.Round(center.X - rectWidth / 2.0), 0, Math.Max(0, trainImageSize.Width - rectWidth));
        var y = Math.Clamp((int)Math.Round(center.Y - rectHeight / 2.0), 0, Math.Max(0, trainImageSize.Height - rectHeight));
        return new Rect(x, y, rectWidth, rectHeight);
    }
}
