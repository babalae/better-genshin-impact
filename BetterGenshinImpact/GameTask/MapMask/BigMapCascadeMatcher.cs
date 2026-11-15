using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Layer;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 大地图五级定位：缩放读数、选中楼层、锚点几何、地表 SIFT、分层 SIFT。
/// 每帧只提取一次截图特征；分层特征按需提取，仅在内存中限量缓存。
/// 同一个 worker 串行启动各帧；帧内三个分支并行，全部退出后才允许重置或释放资源。
/// </summary>
public sealed class BigMapCascadeMatcher : IDisposable
{
    /// <summary>地图坐标转换及地表特征所属场景。</summary>
    private readonly SceneBaseMap _sceneMap;

    /// <summary>提瓦特地表资源为原生场景的八分之一；独立地图使用原生特征坐标。</summary>
    private double SurfaceScale => _sceneMap.Type == MapTypes.Teyvat ? 8d : 1d;

    /// <summary>L1 从成功定位学习实际缩放比例，供 L3 验证几何假设。</summary>
    private readonly BigMapZoom _zoom;

    /// <summary>L3 独立锚点定位，不依赖 L4/L5 的成功结果。</summary>
    private readonly BigMapAnchorMatcher _anchorMatcher;

    /// <summary>当前计算图像的 UI 缩放比例，用于去除输入分辨率影响。</summary>
    private double _queryAssetScale;

    /// <summary>当前计算图像宽度，用于从成功视口校准比例。</summary>
    private int _queryWidth;

    /// <summary>实例独享的 SIFT，避免与小地图线程共用 Feature2D。</summary>
    private readonly SIFT _sift = SIFT.Create(6000);

    /// <summary>复用地表全图的搜索索引，避免每帧重复建立 KD 树。</summary>
    private FlannBasedMatcher? _surfaceMatcher;

    /// <summary>诊断日志。</summary>
    private readonly ILogger<BigMapCascadeMatcher> _logger = App.GetLogger<BigMapCascadeMatcher>();

    /// <summary>上帧地表定位范围，单位为 256 级地图像素，用于局部搜索。</summary>
    private Rect _previousSurfaceRect;

    /// <summary>上次成功定位的位置，为地表切地下提供候选排序先验，不排除其它地区。</summary>
    private Point2f? _lastPosition;
    /// <summary>创建地图定位器，不持有截图。</summary>
    public BigMapCascadeMatcher(SceneBaseMap sceneMap, string? anchorCachePath = null)
    {
        _sceneMap = sceneMap;
        _zoom = new BigMapZoom(sceneMap.Type == MapTypes.Teyvat);
        _anchorMatcher = new BigMapAnchorMatcher(sceneMap, anchorCachePath);
        LayerMatcher = new BigMapLayerMatcher(sceneMap, _sift);
    }

    /// <summary>L5 分层组件，统一管理层资源、候选及连续帧状态。</summary>
    internal BigMapLayerMatcher LayerMatcher { get; }

    /// <summary>可注入的当前选中楼层读取器；无信号时仍尝试地表定位。</summary>
    public Func<Mat, LayerButtonSignal?>? LayerButtonReader { get; set; }

    /// <summary>最近一帧实际执行的阶段及搜索进度，失败时也可显示。</summary>
    public string LastDiagnostics { get; private set; } = string.Empty;

    /// <summary>L1 最近一帧实际读取的缩放 UI；读取失败时为 null。</summary>
    public BigMapZoomSignal? LastZoomSignal { get; private set; }

    /// <summary>同步兼容入口；生产 worker 使用异步入口，不阻塞捕获线程。</summary>
    public MapMatchResult? Match(Mat greyBigMapMat, Mat? colorBigMapMat = null) =>
        MatchAsync(greyBigMapMat, colorBigMapMat).GetAwaiter().GetResult();

    /// <summary>共享一次截图特征提取；有效坐标先通过 resultReady 发布，返回时已安全回收所有分支。</summary>
    public async Task<MapMatchResult?> MatchAsync(Mat greyBigMapMat, Mat? colorBigMapMat = null,
        CancellationToken cancellationToken = default, Action<MapMatchResult, string>? resultReady = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastDiagnostics = string.Empty;
        LastZoomSignal = null;
        if (greyBigMapMat.Empty() || greyBigMapMat.Type() != MatType.CV_8UC1)
        {
            LastDiagnostics = "输入为空或不是灰度截图";
            return null;
        }

        // L1/L2 是所有并行分支共享的只读帧先验；对外先发布坐标，内部学习及缓存更新在各分支退出后提交。
        LastZoomSignal = BigMapZoom.Read(greyBigMapMat);
        LastDiagnostics = LastZoomSignal is { } zoom ? $"L1: 缩放刻度 {zoom.Position:F3}" : "L1: 缩放滑块未识别";
        var signal = ReadLayerButtonSignal(colorBigMapMat);
        LastDiagnostics += "；L2: " + (signal == null ? "楼层按钮未识别" : signal.IsUnderground
            ? $"地下按钮（相对层序 {signal.DepthFromSurface}）" : "地表按钮");
        LayerMatcher.SetLayerSignal(signal);

        var mapArea = BigMapUiLayout.GetMapArea(greyBigMapMat);
        using var query = BigMapImageFeatures.Prepare(greyBigMapMat, 960, mapArea);
        if (mapArea.Width < greyBigMapMat.Width)
        {
            LastDiagnostics += "；已排除右侧弹框";
        }
        _queryWidth = query.Size.Width;
        _queryAssetScale = Math.Min(query.Size.Width / 1920d, query.Size.Height / 1080d);

        // 第一个 SIFT 分支直接在已有线程池任务中提取；另一个分支异步等待，省去嵌套 Task.Run 的排队。
        var featureReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var featureOwner = 0;
        Task EnsureFeatures(CancellationToken token)
        {
            if (Interlocked.CompareExchange(ref featureOwner, 1, 0) == 0)
            {
                try
                {
                    query.EnsureFeatures(_sift, token);
                    featureReady.SetResult();
                }
                catch (OperationCanceledException)
                {
                    featureReady.SetCanceled(token);
                }
                catch (Exception e)
                {
                    featureReady.SetException(e);
                }
            }
            return featureReady.Task;
        }
        var frameDiagnostics = LastDiagnostics;
        var parallel = await BigMapParallelStages.RunAsync(
            token => Task.FromResult(MatchAnchors(query, signal, token)),
            async token =>
            {
                await EnsureFeatures(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var result = MatchSurface(query, token);
                return new BigMapStageResult(result, query.KeyPoints.Length < 8 ? $"截图特征不足（{query.KeyPoints.Length}）" :
                    result == null ? "地表几何匹配失败" : "地表几何匹配通过");
            },
            async token =>
            {
                await EnsureFeatures(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return LayerMatcher.Match(query, signal, _lastPosition, token);
            }, signal?.IsUnderground == true, cancellationToken, update =>
            {
                // 回调只接收不可变坐标和诊断，不访问仍由其它分支修改的匹配器缓存。
                var result = NormalizeResult(update.Result!, signal);
                resultReady?.Invoke(result, frameDiagnostics + "；" + update.Diagnostics);
            }).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        LastDiagnostics += "；" + parallel.Diagnostics;
        var selected = parallel.Result == null ? null : NormalizeResult(parallel.Result, signal);
        if (selected?.Via == "L3-Anchors")
        {
            var view = selected.Viewport2048;
            _previousSurfaceRect = MapMatchResult.RoundViewport(new Rect2d(view.X / SurfaceScale, view.Y / SurfaceScale, view.Width / SurfaceScale, view.Height / SurfaceScale));
        }
        return Remember(selected, query);
    }

    /// <summary>地下的地表几何只能确定平面坐标，提前发布和最终返回统一标为楼层未确认。</summary>
    private static MapMatchResult NormalizeResult(MapMatchResult result, LayerButtonSignal? signal) =>
        result.Via == "L4-SIFT" && signal?.IsUnderground == true
            ? result with { Floor = null, LayerName = null, Via = "L4-Background" }
            : result;

    /// <summary>地图移动后重新检索候选，保留上一层及已确认锚点以便在新视口快速验证。</summary>
    public void InvalidateView()
    {
        LayerMatcher.InvalidateView();
    }

    /// <summary>仅成功结果更新位置先验，失败帧不会丢掉当前区域。</summary>
    private MapMatchResult? Remember(MapMatchResult? result, BigMapImageFeatures query)
    {
        if (result != null)
        {
            _lastPosition = result.GenshinPosition;
            _zoom.Observe(LastZoomSignal, result.Viewport2048.Width / (double)_queryWidth * _queryAssetScale);
            _anchorMatcher.Observe(query.Appearance, query.MapArea, result);
        }

        return result;
    }

    /// <summary>关闭地图或切换会话时清空位置先验，已提取的地图特征可继续复用。</summary>
    public void Reset()
    {
        _previousSurfaceRect = default;
        LayerMatcher.Reset();
        _lastPosition = null;
        LastDiagnostics = string.Empty;
        LastZoomSignal = null;
        _zoom.Reset();
    }

    /// <summary>L3 的资源失败仅禁用当前锚点分支，仍继续 L4/L5；所有坐标须通过视口校验。</summary>
    private BigMapStageResult MatchAnchors(BigMapImageFeatures query, LayerButtonSignal? signal, CancellationToken token)
    {
        var expectedScale = _zoom.Predict(LastZoomSignal, _queryAssetScale);
        var match = _anchorMatcher.Match(query.Appearance, expectedScale, query.MapArea, token);
        token.ThrowIfCancellationRequested();
        var diagnostics = _anchorMatcher.LastDiagnostics + (expectedScale == null ? string.Empty : "，已使用缩放约束");
        var result = match == null ? null : MapMatchResult.FromViewport(_sceneMap, match.Viewport,
            signal?.IsUnderground == false ? 0 : null, null, match.Confidence, "L3-Anchors");
        return new BigMapStageResult(result, diagnostics);
    }

    /// <summary>读取选中按钮，识别异常只影响本帧的层先验。</summary>
    private LayerButtonSignal? ReadLayerButtonSignal(Mat? colorMat)
    {
        if (colorMat == null || colorMat.Empty() || LayerButtonReader == null)
        {
            return null;
        }

        try
        {
            return LayerButtonReader(colorMat);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "层级按钮识别异常，本帧不使用层先验");
            return null;
        }
    }

    /// <summary>地表优先局部匹配，局部验证失败再回退全图。</summary>
    private MapMatchResult? MatchSurface(BigMapImageFeatures query, CancellationToken token)
    {
        var surface = _sceneMap.Type == MapTypes.Teyvat
            ? BigMapTeyvat256Layer.GetInstance(_sceneMap)
            : _sceneMap.Layers.FirstOrDefault(layer => layer.Floor == 0);
        if (surface == null) return null;
        var scale = SurfaceScale;
        BigMapFeatureMatch? match = null;
        if (_previousSurfaceRect != default && surface.SplitBlocks.Length > 0)
        {
            var size = new Size((int)(_sceneMap.MapSize.Width / scale), (int)(_sceneMap.MapSize.Height / scale));
            var (r0, r1, c0, c1) = KeyPointFeatureBlockHelper.GetCellRange(
                size, surface.SplitBlocks.Length, surface.SplitBlocks[0].Length, _previousSurfaceRect);
            var block = KeyPointFeatureBlockHelper.MergeFeaturesInRange(
                surface.SplitBlocks, surface.TrainDescriptors, r0 - 1, r1 + 1, c0 - 1, c1 + 1);
            using var descriptors = block.Descriptor;
            if (descriptors != null)
            {
                match = BigMapFeatureGeometry.Match(query, block.KeyPointArray, descriptors, cancellationToken: token);
            }
        }

        if (match == null)
        {
            if (_surfaceMatcher == null)
            {
                _surfaceMatcher = BigMapFeatureGeometry.CreateMatcher();
                _surfaceMatcher.Add([surface.TrainDescriptors]);
            }

            match = BigMapFeatureGeometry.Match(query, surface.TrainKeyPoints, _surfaceMatcher, cancellationToken: token);
        }
        if (match == null || !MapMatchResult.IsViewportValid(_sceneMap, match.Viewport, scale))
        {
            _previousSurfaceRect = default;
            return null;
        }

        token.ThrowIfCancellationRequested();
        _previousSurfaceRect = MapMatchResult.RoundViewport(match.Viewport);
        var rect = match.Viewport;
        return MapMatchResult.FromViewport(_sceneMap, new Rect2d(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale),
            0, null, match.Confidence, "L4-SIFT");
    }

    /// <summary>释放本实例拥有的原生特征和检测器，不释放场景共享的地表特征。</summary>
    public void Dispose()
    {
        LayerMatcher.Dispose();
        _surfaceMatcher?.Dispose();
        _anchorMatcher.Dispose();
        _sift.Dispose();
    }
}
