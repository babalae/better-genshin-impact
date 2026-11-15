using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// L5 分层定位：统一持有层清单、候选检索、连续帧先验和有界特征缓存。
/// 只在截图特征提取完成后调用；借用协调器的 SIFT，不负责释放它。
/// </summary>
internal sealed class BigMapLayerMatcher(SceneBaseMap sceneMap, Feature2D sift) : IDisposable
{
    /// <summary>每帧分层探索的软预算；单层匹配完成后检查。</summary>
    private const int LayerExploreBudgetMs = 600;

    /// <summary>限制分层特征的内存占用。</summary>
    private const int MaxCachedLayers = 12;

    /// <summary>限制每帧探索数，并将下一帧的搜索起点向后推进。</summary>
    private const int MaxLayerExploreCount = 12;

    /// <summary>冷启动先搜索较小层图，减少全大陆逐层特征提取的开销。</summary>
    private const int CoarseLayerMaxSide = 512;

    /// <summary>较小层图一轮未命中后，使用高分辨率补充细节不足的候选。</summary>
    private const int DetailedLayerMaxSide = 1536;

    /// <summary>当前场景的资源路径及坐标转换。</summary>
    private readonly SceneBaseMap _sceneMap = sceneMap;

    /// <summary>借用的特征提取器，与本帧截图特征提取串行使用。</summary>
    private readonly Feature2D _sift = sift;

    /// <summary>层资源读取及特征提取失败的诊断日志。</summary>
    private readonly ILogger<BigMapLayerMatcher> _logger = App.GetLogger<BigMapLayerMatcher>();

    /// <summary>只含元数据的分层清单，不加载小地图匹配器及整张彩色地图。</summary>
    private List<LayerInfo>? _layers;

    /// <summary>按最近使用顺序维护的有界特征缓存。</summary>
    private readonly LinkedList<(LayerInfo Layer, int MaxSide, BigMapImageFeatures Features)> _layerFeatures = new();

    /// <summary>缺失或无法读取的层，本实例不再重复读取。</summary>
    private readonly HashSet<string> _unavailableLayers = [];

    /// <summary>上帧确认的分层地图。</summary>
    private LayerInfo? _lastLayer;

    /// <summary>上帧按钮状态，切层时使粘层失效。</summary>
    private LayerButtonSignal? _lastSignal;

    /// <summary>分层探索游标，避免每帧只搜索清单前几层。</summary>
    private int _nextLayerIndex;

    /// <summary>本轮搜索的层图尺度，完整遍历后在快速搜索与细节补充之间切换。</summary>
    private int _layerSearchMaxSide = CoarseLayerMaxSide;

    /// <summary>L5 全局粗检索产生的稳定候选顺序，同一视口跨帧沿用以保证搜索推进。</summary>
    private Dictionary<string, int>? _layerCandidateRanking;

    /// <summary>上一成功层所使用的特征尺度，连续帧沿用同一份缓存。</summary>
    private int _lastLayerMaxSide = CoarseLayerMaxSide;

    /// <summary>本帧几何定位成功但外观核验失败的原因，供诊断候选被拒绝的位置。</summary>
    private string _layerRejection = string.Empty;

    /// <summary>分支启动前更新层先验；切层时放弃上一层，并重新检索候选。</summary>
    internal void SetLayerSignal(LayerButtonSignal? signal)
    {
        if (signal != _lastSignal)
        {
            _lastLayer = null;
            InvalidateView();
            _lastSignal = signal;
        }
    }

    /// <summary>先验证上一层，再按候选顺序探索；只由本帧的 L5 分支调用。</summary>
    internal BigMapStageResult Match(BigMapImageFeatures query, LayerButtonSignal? signal,
        Point2f? position, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _layerRejection = string.Empty;
        if (_lastLayer != null && signal?.IsUnderground != false)
        {
            var sticky = TryMatchLayer(query, _lastLayer, _lastLayerMaxSide, token);
            if (sticky != null)
            {
                return new BigMapStageResult(ConfirmLayer(sticky, signal), "上次分层匹配通过");
            }
            _lastLayer = null;
            InvalidateView();
        }
        if (query.KeyPoints.Length < 8)
        {
            return new BigMapStageResult(null, $"截图特征不足（{query.KeyPoints.Length}）");
        }
        var layered = MatchLayers(query, signal, position, token, out var diagnostics);
        return new BigMapStageResult(layered == null ? null : ConfirmLayer(layered, signal), diagnostics);
    }

    /// <summary>地图移动后重新检索候选，保留上一层及层特征以便在新视口快速验证。</summary>
    internal void InvalidateView()
    {
        _nextLayerIndex = 0;
        _layerSearchMaxSide = CoarseLayerMaxSide;
        _layerCandidateRanking = null;
    }

    /// <summary>不同层可能保留同一地表背景，只有层序号一致时才报告确认楼层。</summary>
    private static MapMatchResult ConfirmLayer(MapMatchResult result, LayerButtonSignal? signal) =>
        signal is { DepthFromSurface: > 0 } && result.Floor != -signal.DepthFromSurface
            ? result with { Floor = null, LayerName = null, Via = "L5-Background" }
            : result;

    /// <summary>关闭地图时清除会话先验，已提取的层特征保留到实例释放。</summary>
    internal void Reset()
    {
        _lastLayer = null;
        _lastSignal = null;
        _layerRejection = string.Empty;
        InvalidateView();
    }

    /// <summary>优先验证上一层与当前位置附近的层，其余候选跨帧轮转探索。</summary>
    private MapMatchResult? MatchLayers(BigMapImageFeatures query, LayerButtonSignal? signal,
        Point2f? position, CancellationToken token, out string diagnostics)
    {
        token.ThrowIfCancellationRequested();
        diagnostics = string.Empty;
        var layers = GetLayeredMaps();
        if (layers.Count == 0)
        {
            diagnostics = "未加载到分层素材";
            return null;
        }

        // 按钮列序号与素材 Floor 不总是一致（存在正楼层），仅用于排序，不能排除其它层。
        if (layers.Count > MaxLayerExploreCount && _layerCandidateRanking == null)
        {
            _layerCandidateRanking = CandidateIndex.Get(layers, token).Rank(query, token);
            diagnostics += $"全局粗检索 {_layerCandidateRanking.Count} 个候选，";
        }
        var candidates = layers.OrderBy(l => _layerCandidateRanking?.GetValueOrDefault(l.LayerId, int.MaxValue) ?? int.MaxValue)
            .ThenBy(l => signal is { DepthFromSurface: > 0 } && l.Floor != -signal.DepthFromSurface ? 1 : 0)
            .ThenBy(l => position is { } p ? l.DistanceTo(p) : 0)
            .ToList();

        var sw = Stopwatch.StartNew();
        var tried = 0;
        for (var i = 0; i < Math.Min(MaxLayerExploreCount, candidates.Count); i++)
        {
            token.ThrowIfCancellationRequested();
            var layer = candidates[_nextLayerIndex % candidates.Count];
            var maxSide = _layerSearchMaxSide;
            _nextLayerIndex = (_nextLayerIndex + 1) % candidates.Count;
            if (_nextLayerIndex == 0)
            {
                // 快速层图不足以表示高度放大的局部细节时，下一轮仍有高分辨率回退。
                _layerSearchMaxSide = maxSide == CoarseLayerMaxSide ? DetailedLayerMaxSide : CoarseLayerMaxSide;
            }
            var result = TryMatchLayer(query, layer, maxSide, token);
            tried++;
            if (result != null)
            {
                diagnostics += $"分层通过（本帧尝试 {tried} 层）";
                // 只命中其它层的公共背景时继续推进探索，不能永远粘在未经确认的楼层上。
                _lastLayer = signal is { DepthFromSurface: > 0 } && layer.Floor != -signal.DepthFromSurface ? null : layer;
                _lastLayerMaxSide = maxSide;
                return result;
            }

            if (sw.ElapsedMilliseconds >= LayerExploreBudgetMs)
            {
                break;
            }
        }

        diagnostics += $"分层搜索 {tried}/{candidates.Count}（{_layerSearchMaxSide}px，游标 {_nextLayerIndex}）";
        if (_layerRejection.Length > 0)
        {
            diagnostics += $"，{_layerRejection}";
        }
        return null;
    }

    /// <summary>匹配并将缩小后的层图视口按 Scale 的倒数转换为游戏及 2048 坐标。</summary>
    private MapMatchResult? TryMatchLayer(BigMapImageFeatures query, LayerInfo layer, int maxSide, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var features = GetLayerFeatures(layer, maxSide, token);
        if (features == null)
        {
            return null;
        }

        // 小层图只覆盖截图的一部分，允许更少内点占比，再通过整片外观排除错误层。
        var match = BigMapFeatureGeometry.Match(query, features.KeyPoints, features.Descriptors, 0.2, features.Size, token);
        if (match == null)
        {
            return null;
        }

        var appearance = BigMapFeatureGeometry.CompareAppearance(query, features, match.Viewport, token);
        if (appearance.Correlation < 0.75 || appearance.MeanDifference > 30)
        {
            _layerRejection = $"{layer.Name} 外观不符（相关度 {appearance.Correlation:F2}，灰度差 {appearance.MeanDifference:F1}）";
            return null;
        }

        var viewport = layer.ToViewport2048(match.Viewport, features.ImageScale, _sceneMap);
        return MapMatchResult.FromViewport(_sceneMap, viewport, layer.Floor, layer.Name,
            match.Confidence, "L5-LayerSIFT", layer.Scale);
    }

    /// <summary>只反序列化元数据，过滤缺失图像、重复项及非法缩放。</summary>
    internal List<LayerInfo> GetLayeredMaps()
    {
        if (_layers != null)
        {
            return _layers;
        }

        _layers = [];
        var directory = Path.Combine(Global.Absolute(@"Assets\Map"), _sceneMap.Type.ToString());
        if (!Directory.Exists(directory))
        {
            return _layers;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order())
        {
            try
            {
                // 与现有分层模型保持 System.Text.Json 格式一致，不触发 LoadLayers 的图片加载。
                var entries = JsonSerializer.Deserialize<List<LayerInfo>>(File.ReadAllText(path));
                if (entries != null)
                {
                    foreach (var entry in entries) entry.MapType = _sceneMap.Type;
                    _layers.AddRange(entries.Where(l => l.Floor != 0 && l.Scale > 0 &&
                        float.IsFinite(l.Scale) && float.IsFinite(l.Left) && float.IsFinite(l.Top) &&
                        !string.IsNullOrWhiteSpace(l.LayerId) && File.Exists(l.ImagePath)));
                }
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                _logger.LogDebug(e, "分层地图清单 {Path} 加载失败", path);
            }
        }

        // 独立地图的早期素材以整张楼层图提供，仍需进入 L5 并使用本场景原点。
        if (_sceneMap.Type != MapTypes.Teyvat)
        {
            foreach (var path in Directory.EnumerateFiles(directory, $"{_sceneMap.Type}_*_1024.*"))
            {
                if (Path.GetExtension(path) is not (".png" or ".webp")) continue;
                var parts = Path.GetFileNameWithoutExtension(path).Split('_');
                if (parts.Length != 3 || !int.TryParse(parts[1], out var floor) || floor == 0) continue;
                _layers.Add(new LayerInfo
                {
                    LayerId = Path.GetFileNameWithoutExtension(path), Name = $"{_sceneMap.Type} {floor}",
                    Floor = floor, MapType = _sceneMap.Type, LegacyImagePath = path,
                    Left = _sceneMap.MapOriginInImageCoordinate.X, Top = _sceneMap.MapOriginInImageCoordinate.Y
                });
            }
        }
        _layers = _layers.DistinctBy(l => l.LayerId).ToList();
        return _layers;
    }

    /// <summary>加载单层并维护 LRU；不向外部资源包目录写入派生特征文件。</summary>
    private BigMapImageFeatures? GetLayerFeatures(LayerInfo layer, int maxSide, CancellationToken token)
    {
        for (var node = _layerFeatures.First; node != null; node = node.Next)
        {
            if (node.Value.Layer.LayerId == layer.LayerId && node.Value.MaxSide == maxSide)
            {
                _layerFeatures.Remove(node);
                _layerFeatures.AddFirst(node);
                return node.Value.Features;
            }
        }

        if (_unavailableLayers.Contains(layer.LayerId))
        {
            return null;
        }

        try
        {
            using var image = new Mat(layer.ImagePath, ImreadModes.Grayscale);
            if (!image.Empty())
            {
                var features = BigMapImageFeatures.Extract(_sift, image, maxSide, token);
                _layerFeatures.AddFirst((layer, maxSide, features));
                if (_layerFeatures.Count > MaxCachedLayers)
                {
                    _layerFeatures.Last!.Value.Features.Dispose();
                    _layerFeatures.RemoveLast();
                }

                return features;
            }
        }
        catch (Exception e) when (e is IOException or OpenCVException)
        {
            _logger.LogDebug(e, "层 {LayerId} 特征提取失败", layer.LayerId);
        }

        _unavailableLayers.Add(layer.LayerId);
        return null;
    }

    /// <summary>释放本实例的层特征，不释放借用的 SIFT 或进程共享的粗检索索引。</summary>
    public void Dispose()
    {
        foreach (var (_, _, features) in _layerFeatures)
        {
            features.Dispose();
        }
        _layerFeatures.Clear();
    }

    /// <summary>大地图分层定位所需的轻量元数据，不包含小地图专用的匹配器和 Mat。</summary>
    internal sealed class LayerInfo
    {
        /// <summary>供分层清单的 System.Text.Json 反序列化使用。</summary>
        public LayerInfo()
        {
        }

        /// <summary>外部资源包中的层标识。</summary>
        public string LayerId { get; set; } = string.Empty;

        /// <summary>显示名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>每游戏单位对应的原始层图像素数。</summary>
        public float Scale { get; set; } = 1;

        /// <summary>资源包中的楼层号，允许正楼层。</summary>
        public int Floor { get; set; }

        /// <summary>层图左上角的游戏 Y 坐标。</summary>
        public float Top { get; set; }

        /// <summary>层图左上角的游戏 X 坐标。</summary>
        public float Left { get; set; }

        /// <summary>超大层图使用 PNG。</summary>
        public bool IsOverSize { get; set; }

        /// <summary>资源所属场景，由清单加载器指定，禁止跨地图读取同名层。</summary>
        [JsonIgnore]
        public MapTypes MapType { get; set; } = MapTypes.Teyvat;

        /// <summary>兼容只有整图素材的旧式楼层。</summary>
        [JsonIgnore]
        public string? LegacyImagePath { get; set; }

        /// <summary>兼容普通 WebP 和超大 PNG 的资源路径。</summary>
        [JsonIgnore]
        public string ImagePath => LegacyImagePath ?? Path.Combine(Global.Absolute(@"Assets\Map"), MapType.ToString(),
            LayerId + "_gray" + (IsOverSize ? ".png" : ".webp"));

        /// <summary>位置先验到层图左上角的距离，仅用于候选排序。</summary>
        public double DistanceTo(Point2f position) => Math.Pow(Left - position.X, 2) + Math.Pow(Top - position.Y, 2);

        /// <summary>缩小层图像素 → 游戏单位（除以 Scale）→ 场景图像素。</summary>
        internal Rect2d ToViewport2048(Rect2d rect, double imageScale, SceneBaseMap map)
        {
            var pixelsPerUnit = Scale * imageScale;
            var topLeft = map.ConvertGenshinMapCoordinatesToImageCoordinates(
                new Point2f((float)(Left - rect.X / pixelsPerUnit), (float)(Top - rect.Y / pixelsPerUnit)));
            var bottomRight = map.ConvertGenshinMapCoordinatesToImageCoordinates(
                new Point2f((float)(Left - rect.Right / pixelsPerUnit), (float)(Top - rect.Bottom / pixelsPerUnit)));
            return new Rect2d(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        }
    }

    /// <summary>以低分辨率地形索引检索可能的分层，冷启动无需逐帧遍历整个大陆。</summary>
    private sealed class CandidateIndex
    {
        /// <summary>同一素材目录的索引在进程中共享，避免切换地图会话后重新读取所有图片。</summary>
        private static readonly Dictionary<string, CandidateIndex> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>保护共享索引的创建及原生匹配器访问。</summary>
        private static readonly object _cacheLock = new();

        /// <summary>粗检索层图的长边上限；每层只保留少量特征，不作为最终定位结果。</summary>
        private const int ReferenceMaxSide = 256;

        /// <summary>索引中有效的层及其计算尺度大小。</summary>
        private readonly List<(string Id, Size Size)> _layers = [];

        /// <summary>每个训练描述子对应的层序号。</summary>
        private readonly List<int> _pointLayers = [];

        /// <summary>每个训练描述子在所属层中的位置。</summary>
        private readonly List<Point2f> _points = [];

        /// <summary>进程级粗检索描述子；与索引共同保留，不写入外部地图资源目录。</summary>
        private readonly Mat _descriptors = new();

        /// <summary>所有层共享的地形近邻索引。</summary>
        private readonly FlannBasedMatcher _matcher = BigMapFeatureGeometry.CreateMatcher();

        /// <summary>仅从存在的素材建立粗索引，最终楼层仍须由独立的 L5 几何和外观核验确认。</summary>
        private CandidateIndex(IReadOnlyList<LayerInfo> layers, CancellationToken token)
        {
            using var sift = SIFT.Create(500);
            var descriptors = new List<Mat>();
            try
            {
                foreach (var layer in layers)
                {
                    token.ThrowIfCancellationRequested();
                    using var image = new Mat(layer.ImagePath, ImreadModes.Grayscale);
                    if (image.Empty())
                    {
                        continue;
                    }
                    using var features = BigMapImageFeatures.Extract(sift, image, ReferenceMaxSide, token);
                    if (features.KeyPoints.Length < 8)
                    {
                        continue;
                    }
                    var index = _layers.Count;
                    _layers.Add((layer.LayerId, features.Size));
                    _pointLayers.AddRange(Enumerable.Repeat(index, features.KeyPoints.Length));
                    _points.AddRange(features.KeyPoints.Select(p => p.Pt));
                    descriptors.Add(features.Descriptors.Clone());
                }
                if (descriptors.Count > 0)
                {
                    Cv2.VConcat(descriptors, _descriptors);
                    _matcher.Add([_descriptors]);
                    _matcher.Train();
                    token.ThrowIfCancellationRequested();
                }
            }
            catch
            {
                _matcher.Dispose();
                _descriptors.Dispose();
                throw;
            }
            finally
            {
                foreach (var descriptor in descriptors)
                {
                    descriptor.Dispose();
                }
            }
        }

        /// <summary>获取当前资源目录的共享索引；测试修改候选清单时不会从索引直接返回楼层。</summary>
        internal static CandidateIndex Get(IReadOnlyList<LayerInfo> layers, CancellationToken token = default)
        {
            var directory = Path.GetDirectoryName(layers[0].ImagePath)!;
            while (!Monitor.TryEnter(_cacheLock, 50))
            {
                token.ThrowIfCancellationRequested();
            }
            try
            {
                token.ThrowIfCancellationRequested();
                if (!_cache.TryGetValue(directory, out var index))
                {
                    _cache[directory] = index = new CandidateIndex(layers, token);
                }
                return index;
            }
            finally
            {
                Monitor.Exit(_cacheLock);
            }
        }

        /// <summary>按几何一致的特征支持数排序；保留多个近邻，避免公共背景在 Lowe 检验时彼此排斥。</summary>
        internal Dictionary<string, int> Rank(BigMapImageFeatures query, CancellationToken token = default)
        {
            while (!Monitor.TryEnter(_cacheLock, 50))
            {
                token.ThrowIfCancellationRequested();
            }
            try
            {
                token.ThrowIfCancellationRequested();
                if (_descriptors.Empty() || query.Descriptors.Empty())
                {
                    return [];
                }
                var groups = new Dictionary<int, List<DMatch>>();
                for (var offset = 0; offset < query.Descriptors.Rows; offset += 128)
                {
                    token.ThrowIfCancellationRequested();
                    using var batch = query.Descriptors.RowRange(offset, Math.Min(offset + 128, query.Descriptors.Rows));
                    foreach (var neighbors in _matcher.KnnMatch(batch, Math.Min(8, _descriptors.Rows)))
                    {
                        var used = new HashSet<int>();
                        foreach (var match in neighbors)
                        {
                            var layer = _pointLayers[match.TrainIdx];
                            if (match.Distance > 260 || !used.Add(layer))
                            {
                                continue;
                            }
                            if (!groups.TryGetValue(layer, out var matches))
                            {
                                groups[layer] = matches = [];
                            }
                            matches.Add(new DMatch(match.QueryIdx + offset, match.TrainIdx, match.Distance));
                        }
                    }
                }

                var candidates = new List<(string Id, double Support)>();
                foreach (var (index, matches) in groups)
                {
                    token.ThrowIfCancellationRequested();
                    var geometry = BigMapFeatureGeometry.Fit(
                        matches.Select(m => query.KeyPoints[m.QueryIdx].Pt).ToArray(),
                        matches.Select(m => _points[m.TrainIdx]).ToArray(), query.Size, 0.2, _layers[index].Size, token);
                    if (geometry != null)
                    {
                        candidates.Add((_layers[index].Id, geometry.Confidence * matches.Count));
                    }
                }
                return candidates.OrderByDescending(c => c.Support).Select((c, i) => (c.Id, Rank: i))
                    .ToDictionary(c => c.Id, c => c.Rank);
            }
            finally
            {
                Monitor.Exit(_cacheLock);
            }
        }
    }
}
