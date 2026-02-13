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
}
