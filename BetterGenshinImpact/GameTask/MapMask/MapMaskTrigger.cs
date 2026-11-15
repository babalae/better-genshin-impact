using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Rect = System.Windows.Rect;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 地图遮罩触发器
/// </summary>
public class MapMaskTrigger : ITaskTrigger
{
    private readonly ILogger<MapMaskTrigger> _logger = App.GetLogger<MapMaskTrigger>();

    public string Name => "地图遮罩";
    public bool IsEnabledByConfig => _maskConfig.Enabled;
    public int Priority => 1; // 低优先级
    public bool IsExclusive => false;

    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    /// <summary>大地图持续定位、主界面小地图及关图清理都依赖截图，不受界面稳定 30 秒的限制。</summary>
    public bool SupportsGameUi(GameUiCategory category) => true;

    /// <summary>
    /// 本触发器所属的运行环境。只借用其中的遮罩单例（MaskWindowMapState），并据 IsStopping 判断后台结果是否作废
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 地图遮罩配置
    /// </summary>
    private readonly MapMaskConfig _maskConfig;

    /// <summary>检测地图移动，不让旧帧继续占用计算。</summary>
    private readonly BigMapFrameTracker _frameTracker = new();

    /// <summary>已知地图视口的相邻帧跟踪，拖动时直接刷新点位，不等待全局匹配。</summary>
    private readonly BigMapMotionTracker _motionTracker = new();

    /// <summary>右下角场景标题的后台读取器。</summary>
    private readonly BigMapSceneReader _sceneReader = new();

    /// <summary>已确认的场景，原子读取供小地图任务使用。</summary>
    private int _currentMapType = (int)MapTypes.Teyvat;

    /// <summary>保护当前帧取消源，捕获线程只取消，计算 worker 负责释放。</summary>
    private readonly object _bigMapCancellationLock = new();

    /// <summary>当前正在执行的一帧；地图移动、关图或停用时立即取消。</summary>
    private CancellationTokenSource? _bigMapCancellation;

    /// <summary>地图内容修订号，防止同一地图会话中的旧视口覆盖新截图。</summary>
    private long _bigMapRevision;

    /// <summary>最近一次地图变化的单调时钟，用于移动后短暂稳定检测。</summary>
    private long _lastMapChangeTick;

    /// <summary>最近一次入队的修订及时间，稳定帧不反复取消正在进行的搜索。</summary>
    private long _lastEnqueuedRevision = -1;

    /// <summary>稳定视口的重试间隔。</summary>
    private long _lastEnqueuedTick;

    /// <summary>已完成绝对定位的修订，静止成功帧无需反复启动三路搜索。</summary>
    private long _lastMatchedRevision = -1;

    /// <summary>本次未完成绝对定位的起点，包含稳定等待、取消回收、排队和初始化。</summary>
    private long _locateStartedTick;

    /// <summary>用单调时钟限流，系统时间调整不能导致捕获永久跳过。</summary>
    private long _previousCaptureTick;



    /// <summary>当前地图会话编号，关闭地图后丢弃仍在计算的旧帧。</summary>
    private int _bigMapGeneration;

    /// <summary>捕获线程记录的地图打开状态。</summary>
    private bool _inBigMap;

    private readonly NavigationInstance _navigationInstance = new();

    /// <summary>小地图的位置先验所属场景，切图后不使用上一坐标系的局部搜索中心。</summary>
    private MapTypes? _miniMapScene;

    /// <summary>
    /// 创建地图遮罩触发器
    /// </summary>
    /// <param name="runtime">本触发器所属的运行环境</param>
    /// <param name="config">配置根</param>
    public MapMaskTrigger(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _maskConfig = config.MapMaskConfig;
    }


    private sealed class ComputeWorkItem : IDisposable
    {
        public required string MapMatchingMethod { get; init; }

        /// <summary>本次计算所属坐标系；大地图读取标题后确定，小地图在入队时固定。</summary>
        public MapTypes MapType { get; set; }
        public Mat? Mat { get; set; }

        /// <summary>
        /// 彩色截图（大地图层级按钮识别用），仅大地图计算携带
        /// </summary>
        public Mat? ColorMat { get; set; }

        /// <summary>截图所属地图会话，仅大地图使用。</summary>
        public int BigMapGeneration { get; init; }

        /// <summary>同一地图会话中的视口修订号。</summary>
        public long BigMapRevision { get; init; }

        /// <summary>截图进入触发器的时间，供 UI 统计截图到显示的延迟。</summary>
        public long CaptureTick { get; init; }

        /// <summary>本次定位请求开始时间，覆盖前置等待及失败重试。</summary>
        public long LocateStartedTick { get; init; }

        public void Dispose()
        {
            Mat?.Dispose();
            Mat = null;
            ColorMat?.Dispose();
            ColorMat = null;
        }
    }

    private ComputeWorkItem? _pendingBigMapCompute;
    private int _bigMapWorkerRunning;
    private ComputeWorkItem? _pendingMiniMapCompute;
    private int _miniMapWorkerRunning;

    /// <summary>
    /// 是否处于启用状态。后台计算线程和 UI 线程据此丢弃停用之后才到达的结果
    /// </summary>
    private volatile bool _active;

    public void OnEnabled(object? options)
    {
        lock (_bigMapCancellationLock)
        {
            _active = true;
        }
    }

    /// <summary>
    /// 停用时丢弃待计算的帧，并隐藏遮罩上的点位
    /// </summary>
    public void OnDisabled()
    {
        lock (_bigMapCancellationLock)
        {
            _active = false;
            Interlocked.Increment(ref _bigMapGeneration);
            CancelBigMapCompute();
            _frameTracker.Reset();
            _motionTracker.Reset();

            var pendingBigMapCompute = Interlocked.Exchange(ref _pendingBigMapCompute, null);
            pendingBigMapCompute?.Dispose();
            var pendingMiniMapCompute = Interlocked.Exchange(ref _pendingMiniMapCompute, null);
            pendingMiniMapCompute?.Dispose();

            // 清空状态和使任务失效必须原子完成，防止旧结果在停用后重新写入。
            // 退役的触发器可能在下一会话的第一帧才收到停用回调，这里只清理借用的地图状态单例，
            // 不使用截图器、输入或窗口；解绑时 GameRuntimeService 也会重置一次
            _runtime.MaskWindowMapState.Reset();
        }

        // 由计算 worker 释放匹配器，避免 UI 线程等待或释放仍在使用的 Mat。
        EnsureBigMapWorkerRunning();
    }

    /// <summary>
    /// 接收每帧截图内容并驱动大地图/小地图的异步定位与UI更新
    /// </summary>
    /// <param name="content">捕获到的画面内容</param>
    public void OnCapture(CaptureContent content)
    {
        var captureTick = Environment.TickCount64;
        if (captureTick - _previousCaptureTick <= 50)
        {
            return;
        }

        _previousCaptureTick = captureTick;

        try
        {
            var region = content.CaptureRectArea;
            var inBigMapUi = content.CurrentGameUiCategory == GameUiCategory.BigMap || Bv.IsInBigMapUi(region);
            if (inBigMapUi != _inBigMap)
            {
                lock (_bigMapCancellationLock)
                {
                    _inBigMap = inBigMapUi;
                    Interlocked.Increment(ref _bigMapGeneration);
                    CancelBigMapCompute();
                    _frameTracker.Reset();
                    _motionTracker.Reset();
                    Interlocked.Exchange(ref _locateStartedTick, 0);
                    Interlocked.Exchange(ref _pendingBigMapCompute, null)?.Dispose();
                    PublishMapState(isInBigMap: inBigMapUi, bigMapViewport: new Rect(0, 0, 0, 0), bigMapDebugText: string.Empty);
                }
            }
            var mapMatchingMethod = _config.PathingConditionConfig.MapMatchingMethod;
            Rect? miniMapViewport = null;

            if (inBigMapUi)
            {
                var tick = Environment.TickCount64;
                if (_frameTracker.HasChanged(region.CacheGreyMat))
                {
                    lock (_bigMapCancellationLock)
                    {
                        var changedRevision = Interlocked.Increment(ref _bigMapRevision);
                        _lastMapChangeTick = tick;
                        Interlocked.CompareExchange(ref _locateStartedTick, captureTick, 0);
                        CancelBigMapCompute();
                        Interlocked.Exchange(ref _pendingBigMapCompute, null)?.Dispose();
                        var trackingStart = Stopwatch.GetTimestamp();
                        if (_motionTracker.TryTrack(region.CacheGreyMat, changedRevision, out var tracked))
                        {
                            PublishMapState(bigMapViewport: new Rect(tracked.X, tracked.Y, tracked.Width, tracked.Height),
                                bigMapDebugText: _maskConfig.ShowBigMapDebugInfo
                                    ? $"拖动/缩放跟踪: {Stopwatch.GetElapsedTime(trackingStart).TotalMilliseconds:F1}ms，绝对坐标待复核"
                                    : string.Empty, bigMapFrameTick: captureTick, bigMapRequestTick: Volatile.Read(ref _locateStartedTick));
                        }
                        else
                        {
                            // 无可靠相对变换时暂时保留已显示的点位，不能每次移动都把它们清空。
                            PublishMapState(bigMapDebugText: _maskConfig.ShowBigMapDebugInfo ? "正在重新定位…" : string.Empty,
                                bigMapFrameTick: captureTick, bigMapRequestTick: Volatile.Read(ref _locateStartedTick));
                        }
                    }
                }
                var revision = Volatile.Read(ref _bigMapRevision);
                // 跟踪已在捕获线程即时发布；昂贵的绝对匹配只在短暂稳定后校准，失败时继续跨帧搜索。
                if (tick - _lastMapChangeTick >= 120 &&
                    (revision != _lastEnqueuedRevision ||
                     revision != Volatile.Read(ref _lastMatchedRevision) &&
                     Volatile.Read(ref _bigMapWorkerRunning) == 0 && tick - _lastEnqueuedTick >= 150))
                {
                    _lastEnqueuedRevision = revision;
                    _lastEnqueuedTick = tick;
                    EnqueueBigMapCompute(new ComputeWorkItem
                    {
                        MapMatchingMethod = mapMatchingMethod,
                        Mat = region.CacheGreyMat.Clone(),
                        ColorMat = region.SrcMat.Clone(),
                        BigMapGeneration = Volatile.Read(ref _bigMapGeneration),
                        BigMapRevision = revision,
                        CaptureTick = captureTick,
                        LocateStartedTick = Volatile.Read(ref _locateStartedTick)
                    });
                }
            }
            else
            {
                // 主界面上展示小地图
                if (_maskConfig.MiniMapMaskEnabled)
                {
                    if (Bv.IsInMainUi(region))
                    {
                        var srcMat = region.SrcMat.Clone();
                        EnqueueMiniMapCompute(new ComputeWorkItem
                        {
                            MapMatchingMethod = mapMatchingMethod,
                            MapType = (MapTypes)Volatile.Read(ref _currentMapType),
                            BigMapGeneration = Volatile.Read(ref _bigMapGeneration),
                            Mat = srcMat
                        });
                    }
                    else
                    {
                        miniMapViewport = new Rect(0, 0, 0, 0);
                    }
                }
            }

            PublishMapState(isInBigMap: inBigMapUi, miniMapViewport: miniMapViewport);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "实时地图定位时发生异常");
        }
    }

    /// <summary>
    /// 入队大地图定位计算，仅保留正在执行与最新任务
    /// </summary>
    /// <param name="workItem">计算任务</param>
    private void EnqueueBigMapCompute(ComputeWorkItem workItem)
    {
        var previous = Interlocked.Exchange(ref _pendingBigMapCompute, workItem);
        previous?.Dispose();

        EnsureBigMapWorkerRunning();
    }

    /// <summary>启动或唤醒唯一的大地图 worker，也负责停用后的资源回收。</summary>
    private void EnsureBigMapWorkerRunning()
    {
        if (Interlocked.Exchange(ref _bigMapWorkerRunning, 1) == 0)
        {
            _ = Task.Run(BigMapWorkerLoopAsync);
        }
    }

    /// <summary>
    /// 入队小地图定位计算，仅保留正在执行与最新任务
    /// </summary>
    /// <param name="workItem">计算任务</param>
    private void EnqueueMiniMapCompute(ComputeWorkItem workItem)
    {
        var previous = Interlocked.Exchange(ref _pendingMiniMapCompute, workItem);
        previous?.Dispose();

        if (Interlocked.Exchange(ref _miniMapWorkerRunning, 1) == 0)
        {
            _ = Task.Run(MiniMapWorkerLoop);
        }
    }

    /// <summary>
    /// 大地图计算工作线程循环
    /// </summary>
    private async Task BigMapWorkerLoopAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _pendingBigMapCompute, null) is { } workItem)
            {
                try
                {
                    await ProcessBigMapComputeAsync(workItem).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 移动后的旧帧正常退出，不发布旧视口，也不把取消显示成定位失败。
                }
                catch (Exception e)
                {
                    _logger.LogDebug(e, "地图遮罩异步计算时发生异常");
                    if (workItem.BigMapRevision == Volatile.Read(ref _bigMapRevision))
                    {
                        PublishMapState(bigMapDebugText: _maskConfig.ShowBigMapDebugInfo ? "定位异常，下一帧重试" : string.Empty);
                    }
                }
                finally
                {
                    workItem.Dispose();
                }
            }
        }
        finally
        {
            // 任意异常路径都恢复 worker 状态，否则后续截图会永久以为计算仍在运行。
            try
            {
                if (!_active || _runtime.IsStopping)
                {
                    _cascadeMatcher?.Dispose();
                    _cascadeMatcher = null;
                    _cascadeSceneMap = null;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _bigMapWorkerRunning, 0);
                if (Volatile.Read(ref _pendingBigMapCompute) != null)
                {
                    EnsureBigMapWorkerRunning();
                }
            }
        }
    }

    /// <summary>只请求协作取消，不释放仍被原生识图函数使用的图像。</summary>
    private void CancelBigMapCompute()
    {
        lock (_bigMapCancellationLock)
        {
            _bigMapCancellation?.Cancel();
        }
    }

    /// <summary>
    /// 小地图计算工作线程循环
    /// </summary>
    private void MiniMapWorkerLoop()
    {
        while (true)
        {
            var workItem = Interlocked.Exchange(ref _pendingMiniMapCompute, null);
            if (workItem == null)
            {
                Interlocked.Exchange(ref _miniMapWorkerRunning, 0);
                if (Volatile.Read(ref _pendingMiniMapCompute) != null && Interlocked.Exchange(ref _miniMapWorkerRunning, 1) == 0)
                {
                    continue;
                }

                return;
            }

            try
            {
                ProcessMiniMapCompute(workItem);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "地图遮罩异步计算时发生异常");
            }
            finally
            {
                workItem.Dispose();
            }
        }
    }

    /// <summary>
    /// 大地图级联匹配器，跨帧保留局部搜索和分层特征缓存，仅计算 worker 访问
    /// </summary>
    private BigMapCascadeMatcher? _cascadeMatcher;

    /// <summary>匹配器所使用的地图实例，配置切换后重新创建匹配器。</summary>
    private SceneBaseMap? _cascadeSceneMap;

    /// <summary>匹配器的位置先验所属会话。</summary>
    private int _matcherGeneration = -1;

    /// <summary>匹配器候选顺序所属的地图视口。</summary>
    private long _matcherRevision = -1;

    /// <summary>
    /// 懒创建级联匹配器，并注入生产环境的选中楼层识别
    /// </summary>
    private BigMapCascadeMatcher GetCascadeMatcher(SceneBaseMap sceneMap)
    {
        if (_cascadeMatcher == null || !ReferenceEquals(_cascadeSceneMap, sceneMap))
        {
            _cascadeMatcher?.Dispose();
            var cacheName = sceneMap.Type == MapTypes.Teyvat ? "big-map-anchors-v1.json" : $"big-map-anchors-{sceneMap.Type}-v1.json";
            _cascadeMatcher = new BigMapCascadeMatcher(sceneMap, Global.Absolute($@"User\MapCache\{cacheName}"))
            {
                LayerButtonReader = LayerButtonGeometry.Resolve
            };
            _cascadeSceneMap = sceneMap;
        }

        return _cascadeMatcher;
    }

    /// <summary>
    /// 执行大地图定位计算并产出UI更新
    /// </summary>
    /// <param name="workItem">计算任务</param>
    private async Task ProcessBigMapComputeAsync(ComputeWorkItem workItem)
    {
        if (workItem.Mat == null || !_active || _runtime.IsStopping ||
            workItem.BigMapGeneration != Volatile.Read(ref _bigMapGeneration) ||
            workItem.BigMapRevision != Volatile.Read(ref _bigMapRevision))
        {
            return;
        }

        // 计算计时覆盖懒加载和匹配器创建；端到端延迟由 UI 读取截图及请求时间后统计。
        var stopwatch = Stopwatch.StartNew();
        var mapType = _sceneReader.Read(workItem.Mat) ?? (MapTypes)Volatile.Read(ref _currentMapType);
        lock (_bigMapCancellationLock)
        {
            if (workItem.BigMapRevision != Volatile.Read(ref _bigMapRevision) ||
                workItem.BigMapGeneration != Volatile.Read(ref _bigMapGeneration) || !_active || _runtime.IsStopping) return;
            workItem.MapType = mapType;
            if (mapType != (MapTypes)Volatile.Read(ref _currentMapType))
            {
                // 切换坐标系时先清理旧视口和光流，点位请求由 UI 根据快照取消并重发。
                Volatile.Write(ref _currentMapType, (int)mapType);
                _motionTracker.Reset();
                // 当前帧已通过修订校验；重建观察状态，使本次绝对定位可立即成为拖动参考。
                _motionTracker.TryTrack(workItem.Mat, workItem.BigMapRevision, out _);
                PublishMapState(bigMapViewport: new Rect(), miniMapViewport: new Rect(), mapType: mapType);
            }
        }
        var sceneMap = (SceneBaseMap)MapManager.GetMap(mapType, "SIFT");
        var matcher = GetCascadeMatcher(sceneMap);
        if (_matcherGeneration != workItem.BigMapGeneration)
        {
            matcher.Reset();
            _matcherGeneration = workItem.BigMapGeneration;
        }

        if (_matcherRevision != workItem.BigMapRevision)
        {
            matcher.InvalidateView();
            _matcherRevision = workItem.BigMapRevision;
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        lock (_bigMapCancellationLock)
        {
            if (workItem.BigMapRevision != Volatile.Read(ref _bigMapRevision) || !_active || _runtime.IsStopping)
            {
                return;
            }
            _bigMapCancellation = cancellation;
        }
        MapMatchResult? result;
        // 发布与回收分别计时，慢分支收尾不再拖住已经可用的地图坐标。
        long lastResultReadyMs = -1;
        try
        {
            result = await matcher.MatchAsync(workItem.Mat, workItem.ColorMat, cancellation.Token, (ready, diagnostics) =>
            {
                var readyMs = stopwatch.ElapsedMilliseconds;
                if (!cancellation.IsCancellationRequested &&
                    PublishBigMapComputeResult(workItem, ready, diagnostics, readyMs))
                {
                    lastResultReadyMs = readyMs;
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (workItem.BigMapRevision == Volatile.Read(ref _bigMapRevision) &&
            workItem.BigMapGeneration == Volatile.Read(ref _bigMapGeneration) && _active && !_runtime.IsStopping)
        {
            PublishMapState(bigMapDebugText: !_maskConfig.ShowBigMapDebugInfo ? string.Empty :
                lastResultReadyMs >= 0 ? "坐标已显示，本帧后续确认超时" : "本帧计算超时，下一帧重试");
            return;
        }
        finally
        {
            lock (_bigMapCancellationLock)
            {
                _bigMapCancellation = null;
            }
            stopwatch.Stop();
        }

        PublishBigMapComputeResult(workItem, result, matcher.LastDiagnostics,
            lastResultReadyMs >= 0 ? lastResultReadyMs : stopwatch.ElapsedMilliseconds,
            lastResultReadyMs >= 0 ? stopwatch.ElapsedMilliseconds - lastResultReadyMs : null);
    }

    /// <summary>提前发布有效坐标，收尾后补齐诊断；会话及修订检查阻止旧结果覆盖移动后的新视口。</summary>
    private bool PublishBigMapComputeResult(ComputeWorkItem workItem, MapMatchResult? result,
        string diagnostics, long readyMilliseconds, long? cleanupMilliseconds = null)
    {
        var debugText = string.Empty;
        Rect rect2048;
        if (result != null)
        {
            // OpenCV Rect → WPF Rect（遮罩视口消费类型）
            rect2048 = new Rect(result.Viewport2048.X, result.Viewport2048.Y, result.Viewport2048.Width, result.Viewport2048.Height);

            if (_maskConfig.ShowBigMapDebugInfo)
            {
                var layerText = result.Floor == null ? "  层: 未确认" :
                    result.Floor != 0 ? $"  层: {result.LayerName ?? "?"}({result.Floor})" : string.Empty;
                var p = result.GenshinPosition;
                debugText = $"坐标: ({p.X:F0}, {p.Y:F0}){layerText}  [{result.Via}]\n" +
                            $"视口: {result.Viewport2048.Width}×{result.Viewport2048.Height}  结果就绪: {readyMilliseconds}ms\n" +
                            diagnostics.Replace("；", "\n");
            }
        }
        else
        {
            rect2048 = new Rect(0, 0, 0, 0);
            if (_maskConfig.ShowBigMapDebugInfo)
            {
                debugText = $"定位失败\n{diagnostics.Replace("；", "\n")}\n计算: {readyMilliseconds}ms";
            }
        }
        if (_maskConfig.ShowBigMapDebugInfo && cleanupMilliseconds is { } cleanup)
        {
            debugText += $"\n后台收尾: {cleanup}ms";
        }

        lock (_bigMapCancellationLock)
        {
            // 用户可能在较慢的全局探索期间移动或关图，旧结果不可污染新视口或下一地图会话。
            if (_active && !_runtime.IsStopping && workItem.BigMapRevision == Volatile.Read(ref _bigMapRevision) &&
                workItem.BigMapGeneration == Volatile.Read(ref _bigMapGeneration))
            {
                if (result != null)
                {
                    var referenceSaved = _motionTracker.SetReference(workItem.Mat!,
                        new Rect2d(rect2048.X, rect2048.Y, rect2048.Width, rect2048.Height), workItem.BigMapRevision);
                    // 提前坐标不能关闭后续确认的重试；只有完整收尾成功才认为本修订处理完成。
                    if (cleanupMilliseconds.HasValue && referenceSaved)
                    {
                        Volatile.Write(ref _lastMatchedRevision, workItem.BigMapRevision);
                        Interlocked.Exchange(ref _locateStartedTick, 0);
                    }
                }
                PublishMapState(bigMapViewport: result != null ? rect2048 : null, bigMapDebugText: debugText,
                    bigMapFrameTick: workItem.CaptureTick, bigMapRequestTick: workItem.LocateStartedTick, mapType: workItem.MapType);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 执行小地图定位计算并产出UI更新
    /// </summary>
    /// <param name="workItem">计算任务</param>
    private void ProcessMiniMapCompute(ComputeWorkItem workItem)
    {
        if (workItem.Mat == null)
        {
            return;
        }

        using var imageRegion = new ImageRegion(workItem.Mat, 0, 0);
        workItem.Mat = null;

        if (_miniMapScene != workItem.MapType)
        {
            _navigationInstance.Reset();
            _miniMapScene = workItem.MapType;
        }
        var miniPoint = _navigationInstance.GetPositionStable(imageRegion, workItem.MapType.ToString(), workItem.MapMatchingMethod, out var result);
        var viewport = new Rect();
        if (miniPoint != default && result != null)
        {
            var sceneMap = MapManager.GetMap(workItem.MapType, workItem.MapMatchingMethod);
            viewport = CreateMiniMapViewport(result, sceneMap);
        }
        PublishMiniMapComputeResult(workItem.MapType, workItem.BigMapGeneration, viewport);
    }

    /// <summary>与场景切换共用临界区，原子校验并发布同一任务的地图类型及小地图视口。</summary>
    internal bool PublishMiniMapComputeResult(MapTypes mapType, int generation, Rect viewport)
    {
        lock (_bigMapCancellationLock)
        {
            if (!_active || _runtime.IsStopping || _inBigMap ||
                generation != Volatile.Read(ref _bigMapGeneration) ||
                mapType != (MapTypes)Volatile.Read(ref _currentMapType)) return false;
            PublishMapState(miniMapViewport: viewport, mapType: mapType);
            return true;
        }
    }

    /// <summary>将小地图定位中心及其显示范围转换为当前场景的原生图像视口。</summary>
    internal static Rect CreateMiniMapViewport(MapMatchResult result, ISceneMap map)
    {
        // 模板预处理把中心裁剪放大到精匹配尺寸；成功层的 Scale 再将层图像素还原为游戏单位。
        // SIFT 接口目前只返回中心，沿用其原有的普通地图比例。
        var layerScale = result.LayerScale ?? 1;
        var mapPixelsPerScreenPixel = (double)MiniMapMatchConfig.ExactSize / MiniMapMatchConfig.OriginalSize / layerScale;

        // 复用场景坐标转换得到原生图像比例，兼容提瓦特及独立地图不同的底图分辨率。
        var origin = map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f(0, 0));
        var unit = map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f(1, 0));
        var viewportSize = MapAssets.MimiMapRect1080P.Width * mapPixelsPerScreenPixel * Math.Abs(unit.X - origin.X);
        return new Rect(result.ImagePosition.X - viewportSize / 2, result.ImagePosition.Y - viewportSize / 2, viewportSize, viewportSize);
    }

    /// <summary>
    /// 写入遮罩窗口地图点位状态。任意线程调用，立即返回；多次写入由状态对象按字段合并，
    /// 遮罩窗口侧合并后在 UI 线程刷新
    /// </summary>
    private void PublishMapState(bool? isInBigMap = null, Rect? bigMapViewport = null, Rect? miniMapViewport = null,
        string? bigMapDebugText = null, long? bigMapFrameTick = null, long? bigMapRequestTick = null, MapTypes? mapType = null)
    {
        // 后台定位线程可能在本运行环境停止后才算完，此时结果已作废，直接丢弃（解绑时状态已由 GameRuntimeService 重置）
        if (_runtime.IsStopping)
        {
            return;
        }

        var mapState = _runtime.MaskWindowMapState;

        if (!_active)
        {
            mapState.Reset();
            return;
        }

        mapState.Update(isInBigMap, bigMapViewport, miniMapViewport, bigMapDebugText, bigMapFrameTick, bigMapRequestTick, mapType);
    }
}
