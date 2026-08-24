using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 独立地图
/// </summary>
public abstract class SceneBaseMap : ISceneMap
{
    public MapTypes Type { get; set; }

    /// <summary>
    /// 地图大小
    /// 当前只用于切割特征点
    /// </summary>
    public Size MapSize { get; set; }

    /// <summary>
    /// 地图原点位置 (在图像坐标系中)
    /// </summary>
    public Point2f MapOriginInImageCoordinate { get; set; }

    /// <summary>
    /// 特征地图图像的块大小
    /// 2048 或者 1024
    /// </summary>
    public Point2f MapImageBlockWidth { get; set; }

    /// <summary>
    /// 特征点拆分行数
    /// </summary>
    public readonly int SplitRow;

    /// <summary>
    /// 特征点拆分列数
    /// </summary>
    public readonly int SplitCol;

    /// <summary>
    /// 特征地图图像的块大小 / 1024 的值，用于坐标系转换
    /// </summary>
    private readonly float _mapImageBlockWidthScale = 0;


    // ReSharper disable once ConvertToPrimaryConstructor
    protected SceneBaseMap(MapTypes type, Size mapSize, Point2f mapOriginInImageCoordinate, int mapImageBlockWidth, int splitRow, int splitCol)
    {
        Type = type;
        MapSize = mapSize;
        MapOriginInImageCoordinate = mapOriginInImageCoordinate;
        _mapImageBlockWidthScale = mapImageBlockWidth / 1024f;
        SplitRow = splitRow;
        SplitCol = splitCol;
    }

    /// <summary>
    /// 分层地图特征列表
    /// 0 是主地图
    /// 此列表保留原有整图/旧楼层资源，继续用于全局匹配和大地图匹配。
    /// </summary>
    private List<BaseMapLayer> _layers = [];

    /// <summary>
    /// 保护原有整图/旧楼层资源的延迟加载。
    /// </summary>
    private readonly object _layersLock = new();

    /// <summary>
    /// 分组分层地图列表。<see langword="null"/> 表示清单尚未读取，空集合表示清单不存在或不可用。
    /// 列表元素只描述层的组织方式；当前 SIFT 实现按需填充其中的特征资源。
    /// </summary>
    private IReadOnlyList<BaseMapLayer>? _groupLayers;

    /// <summary>
    /// 保护分组分层地图清单的首次读取。
    /// </summary>
    private readonly object _groupLayersLock = new();

    public List<BaseMapLayer> Layers
    {
        get
        {
            if (_layers.Count == 0)
            {
                lock (_layersLock)
                {
                    if (_layers.Count == 0)
                    {
                        TaskControl.Logger.LogInformation("[SIFT]地图特征点加载中，预计耗时2秒，请等待...");
                        _layers = BaseMapLayer.LoadLayers(this);
                        TaskControl.Logger.LogInformation("地图特征点加载完成！");
                    }
                }
            }
            return _layers;
        }
        set => _layers = value ?? [];
    }

    /// <summary>
    /// 获取所有分组分层地图。首次访问时只加载清单元数据，不读取各层的具体匹配资源。
    /// </summary>
    internal IReadOnlyList<BaseMapLayer> GroupLayers
    {
        get
        {
            if (_groupLayers == null)
            {
                lock (_groupLayersLock)
                {
                    _groupLayers ??= BaseMapLayer.LoadGroupLayers(this);
                }
            }
            return _groupLayers;
        }
    }

    protected BaseMapLayer MainLayer => Layers[0];

    public readonly Feature2D SiftMatcher = Feature2DFactory.Get(Feature2DType.SIFT);

    protected void ExtractAndSaveFeature(string basePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        var folder = Path.GetDirectoryName(basePath)!;

        string trainKeyPointsPath = Path.Combine(folder, $"{fileName}_SIFT.kp.bin");
        string trainDescriptorsPath = Path.Combine(folder, $"{fileName}_SIFT.mat.png");

        if (File.Exists(trainKeyPointsPath) && File.Exists(trainDescriptorsPath))
        {
            return;
        }

        SiftMatcher.SaveFeatures(basePath, trainKeyPointsPath, trainDescriptorsPath);
    }

    public virtual void WarmUp()
    {
        Console.WriteLine("提前加载地图，层数：" + Layers.Count);
        // 预热阶段只读取分组分层清单，片段的匹配资源仍在进入 HighlightRect 后按需加载。
        _ = GroupLayers.Count;
    }

    public virtual Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        return SiftMatcher.Match(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public virtual Point2f GetBigMapPosition(Mat greyBigMapMat, Point2f expectedCenter)
    {
        var layer = MainLayer;
        if (!IsValidPoint(expectedCenter) || layer.SplitBlocks.Length == 0 || layer.SplitBlocks[0].Length == 0)
        {
            return GetBigMapPosition(greyBigMapMat);
        }

        var searchRect = BuildLocalSearchRect(expectedCenter, greyBigMapMat.Size(), MapSize);
        var result = SiftMatcher.KnnMatchLocal(
            layer.SplitBlocks,
            layer.TrainDescriptors,
            MapSize,
            searchRect,
            greyBigMapMat);
        return result == default ? GetBigMapPosition(greyBigMapMat) : result;
    }

    public virtual Rect GetBigMapRect(Mat greyBigMapMat)
    {
        return SiftMatcher.KnnMatchRect(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    private static bool IsValidPoint(Point2f point)
    {
        return float.IsFinite(point.X) && float.IsFinite(point.Y) &&
               (Math.Abs(point.X) > float.Epsilon || Math.Abs(point.Y) > float.Epsilon);
    }

    protected static Rect BuildLocalSearchRect(Point2f center, Size querySize, Size trainImageSize)
    {
        var width = Math.Min(trainImageSize.Width, Math.Max(querySize.Width * 2.0, trainImageSize.Width / 4.0));
        var height = Math.Min(trainImageSize.Height, Math.Max(querySize.Height * 2.0, trainImageSize.Height / 4.0));
        var rectWidth = (int)Math.Round(width);
        var rectHeight = (int)Math.Round(height);
        var x = Math.Clamp((int)Math.Round(center.X - rectWidth / 2.0), 0, Math.Max(0, trainImageSize.Width - rectWidth));
        var y = Math.Clamp((int)Math.Round(center.Y - rectHeight / 2.0), 0, Math.Max(0, trainImageSize.Height - rectHeight));
        return new Rect(x, y, rectWidth, rectHeight);
    }

    public virtual Point2f GetMiniMapPosition(Mat greyMiniMapMat)
    {
        return GetGlobalMiniMapMatchResult(greyMiniMapMat, null)?.Position ?? default;
    }

    /// <summary>
    /// 不使用上次点位执行全局小地图匹配。
    /// 除原有整图/旧楼层外，也会匹配已经加载的分组分层片段，但不会在此阶段动态加载新片段。
    /// 如果仍保留上次成功结果，则继续沿用相同的楼层、层、分组和高亮范围排序规则。
    /// </summary>
    private MiniMapMatchState? GetGlobalMiniMapMatchResult(Mat greyMiniMapMat, MiniMapMatchState? orderingReference)
    {
        var candidates = OrderMiniMapCandidates(
            Layers.Concat(GetLoadedGroupLayers()),
            orderingReference);

        using var query = SiftMatcher.PrepareFeatureMatchQuery(greyMiniMapMat);
        // 从表到里逐层匹配
        foreach (var layer in candidates)
        {
            try
            {
                var result = SiftMatcher.KnnMatch(layer.TrainKeyPoints, layer.TrainDescriptors, query);
                if (result != default)
                {
                    return CreateMiniMapMatchState(result, layer);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"地图{Type}层数{layer.Floor},特征匹配失败:{e.Message}");
            }
        }

        return null;
    }

    public virtual Point2f GetMiniMapPosition(Mat greyMiniMapMat, float prevX, float prevY)
    {
        if (prevX <= 0 && prevY <= 0)
        {
            return GetMiniMapPosition(greyMiniMapMat);
        }

        var previousMatch = new MiniMapMatchState(
            new Point2f(prevX, prevY),
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            Type.ToString(),
            false);
        return GetMiniMapMatchResult(greyMiniMapMat, previousMatch, previousMatch)?.Position ?? default;
    }

    /// <summary>
    /// 使用完整的上次匹配状态执行局部小地图匹配，并返回本次命中的楼层来源。
    /// </summary>
    /// <param name="greyMiniMapMat">待匹配的小地图图像。</param>
    /// <param name="previousMatch">当前导航实例最近一次成功的匹配状态。</param>
    /// <param name="orderingReference">没有上次点位时，用于延续候选顺序的最近成功匹配状态。</param>
    /// <returns>成功时返回包含参考底图坐标和楼层来源的状态，否则返回 <see langword="null"/>。</returns>
    internal MiniMapMatchState? GetMiniMapMatchResult(
        Mat greyMiniMapMat,
        MiniMapMatchState? previousMatch,
        MiniMapMatchState? orderingReference)
    {
        if (previousMatch == null ||
            (previousMatch.Position.X <= 0 && previousMatch.Position.Y <= 0) ||
            (!string.IsNullOrEmpty(previousMatch.MapName) &&
             !previousMatch.MapName.Equals(Type.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            return GetGlobalMiniMapMatchResult(greyMiniMapMat, orderingReference);
        }

        // Teyvat 使用 2048 级别底图坐标，因此对应 1024 级别的 100 像素范围需要扩张为 200。
        var rangePadding = Type == MapTypes.Teyvat ? 200 : 100;
        var groupLayerCandidates = GroupLayers
            .Where(layer => ContainsExpanded(layer.HighlightRect, previousMatch.Position, rangePadding))
            .ToList();
        var loadedGroupLayerCandidates = new List<BaseMapLayer>(groupLayerCandidates.Count);

        // 命中范围的片段全部尝试加载；已加载片段会直接复用，单个片段失败不会阻断其他候选。
        foreach (var layer in groupLayerCandidates)
        {
            if (layer.EnsureFeatureLoaded())
            {
                loadedGroupLayerCandidates.Add(layer);
            }
        }

        // 临时候选集合不修改原有 Layers 或 GroupLayers 的元素与持久顺序。
        // 排序依次考虑楼层距离、上次命中层、上次命中分组、HighlightRect 距离和稳定层标识。
        var candidates = OrderMiniMapCandidates(
            Layers.Concat(loadedGroupLayerCandidates),
            previousMatch);

        // 当前小地图只提取一次查询特征，随后在所有候选层之间复用，避免每层重复 DetectAndCompute。
        using var query = SiftMatcher.PrepareFeatureMatchQuery(greyMiniMapMat);
        foreach (var layer in candidates)
        {
            Debug.WriteLine("尝试匹配" + string.Concat(candidates.Select(c => c.Name), ","));
            try
            {
                var (keyPoints, descriptors) = (layer.TrainKeyPoints, layer.TrainDescriptors);
                if (SplitRow > 0 || SplitCol > 0)
                {
                    (keyPoints, descriptors) = layer.ChooseBlocks(previousMatch.Position.X, previousMatch.Position.Y);
                }

                var result = SiftMatcher.KnnMatch(keyPoints, descriptors, query, DescriptorMatcherType.BruteForce);
                if (result != default)
                {
                    Debug.WriteLine(layer.Name + " - 匹配 -"  + result.ToString());
                    return CreateMiniMapMatchState(result, layer);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"地图{Type}层数{layer.Floor},特征匹配失败:{e.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// 获取已经成功加载特征资源的分组分层地图快照。
    /// 直接读取内部缓存，确保全局匹配不会仅为枚举候选而触发清单或特征文件加载。
    /// </summary>
    private IReadOnlyList<BaseMapLayer> GetLoadedGroupLayers()
    {
        lock (_groupLayersLock)
        {
            return _groupLayers?.Where(layer => layer.IsFeatureLoaded).ToList() ?? [];
        }
    }

    /// <summary>
    /// 使用统一规则排列普通层和分组分层候选。
    /// 没有可用排序依据时保留输入顺序，即普通层在前、已加载分组分层片段随后。
    /// </summary>
    private List<BaseMapLayer> OrderMiniMapCandidates(
        IEnumerable<BaseMapLayer> candidates,
        MiniMapMatchState? orderingReference)
    {
        var candidateList = candidates.ToList();
        if (orderingReference == null ||
            (!string.IsNullOrEmpty(orderingReference.MapName) &&
             !orderingReference.MapName.Equals(Type.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            return candidateList;
        }

        return candidateList
            .OrderBy(layer => Math.Abs(layer.Floor - orderingReference.Floor))
            .ThenBy(layer => IsSameLayer(layer, orderingReference) ? 0 : 1)
            .ThenBy(layer => IsSameGroup(layer, orderingReference) ? 0 : 1)
            .ThenBy(layer => GetDistanceToHighlightRect(layer, orderingReference.Position))
            .ThenBy(layer => layer.LayerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 将底层匹配坐标和命中层组合成导航实例可保存的状态。
    /// </summary>
    private MiniMapMatchState CreateMiniMapMatchState(Point2f position, BaseMapLayer layer)
    {
        return new MiniMapMatchState(
            position,
            layer.Floor,
            layer.LayerId,
            layer.LayerGroupId,
            layer.Name,
            Type.ToString(),
            layer.IsGroupLayer);
    }

    /// <summary>
    /// 判断坐标是否落在向四周扩张后的矩形内，矩形边界也视为命中。
    /// </summary>
    private static bool ContainsExpanded(Rect rect, Point2f point, int padding)
    {
        return point.X >= rect.X - padding && point.X <= rect.Right + padding &&
               point.Y >= rect.Y - padding && point.Y <= rect.Bottom + padding;
    }

    /// <summary>
    /// 判断候选是否与上次命中的是同一种来源下的同一地图层。
    /// </summary>
    private static bool IsSameLayer(BaseMapLayer layer, MiniMapMatchState previousMatch)
    {
        return !string.IsNullOrEmpty(previousMatch.LayerId) &&
               layer.IsGroupLayer == previousMatch.IsGroupLayer &&
               layer.LayerId.Equals(previousMatch.LayerId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断候选是否与上次命中的层属于同一分组。
    /// </summary>
    private static bool IsSameGroup(BaseMapLayer layer, MiniMapMatchState previousMatch)
    {
        return !string.IsNullOrEmpty(previousMatch.LayerGroupId) &&
               layer.LayerGroupId.Equals(previousMatch.LayerGroupId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 计算上次位置到分组分层实际高亮范围的平方距离；普通层不参与此项排序优先级。
    /// </summary>
    private static double GetDistanceToHighlightRect(BaseMapLayer layer, Point2f point)
    {
        if (!layer.IsGroupLayer)
        {
            return double.MaxValue;
        }

        var rect = layer.HighlightRect;
        var deltaX = Math.Max(rect.X - point.X, Math.Max(0, point.X - rect.Right));
        var deltaY = Math.Max(rect.Y - point.Y, Math.Max(0, point.Y - rect.Bottom));
        return deltaX * deltaX + deltaY * deltaY;
    }

    #region 坐标系转换

    public Point2f? ConvertImageCoordinatesToGenshinMapCoordinates(Point2f imageCoordinates)
    {
        if (imageCoordinates.X == 0 && imageCoordinates.Y == 0)
        {
            return null;
        }
        // 原神坐标系是 1024 级别的，当图像坐标系不是 1024 级别的时候要做转换
        return new Point2f((MapOriginInImageCoordinate.X - imageCoordinates.X) / _mapImageBlockWidthScale,
            (MapOriginInImageCoordinate.Y - imageCoordinates.Y) / _mapImageBlockWidthScale);
    }

    public Rect? ConvertImageCoordinatesToGenshinMapCoordinates(Rect rect)
    {
        if (rect.X == 0 && rect.Y == 0 && rect.Width == 0 && rect.Height == 0)
        {
            return null;
        }
        var center = rect.GetCenterPoint();
        var nullablePoint = ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(center.X, center.Y));
        if (nullablePoint is Point2f p)
        {
            return new Rect((int)(p.X - rect.Width / 2f / _mapImageBlockWidthScale), (int)(p.Y - rect.Height / 2f / _mapImageBlockWidthScale),
                (int)(rect.Width / _mapImageBlockWidthScale), (int)(rect.Height / _mapImageBlockWidthScale));
        }
        return null;
    }

    public Point2f ConvertGenshinMapCoordinatesToImageCoordinates(Point2f? genshinMapCoordinates)
    {
        if (genshinMapCoordinates is Point2f p)
        {
            return new Point2f(MapOriginInImageCoordinate.X - p.X * _mapImageBlockWidthScale,
           MapOriginInImageCoordinate.Y - p.Y * _mapImageBlockWidthScale);
        }
        return default;
    }

    public Rect ConvertGenshinMapCoordinatesToImageCoordinates(Rect? genshinMapRect)
    {
        if (genshinMapRect is Rect rect)
        {
            var (x, y) = ConvertGenshinMapCoordinatesToImageCoordinates(rect.GetCenterPoint());
            return new Rect((int)Math.Round(x - rect.Width / 2f * _mapImageBlockWidthScale),
             (int)Math.Round(y - rect.Height / 2f * _mapImageBlockWidthScale),
             (int)Math.Round(rect.Width * _mapImageBlockWidthScale),
             (int)Math.Round(rect.Height * _mapImageBlockWidthScale));
        }
        return default;
    }

    #endregion
}
