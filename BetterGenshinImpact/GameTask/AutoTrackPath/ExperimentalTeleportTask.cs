using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 实验性大地图传送流程。最终点选与完成确认仍由 <see cref="TpTask"/> 处理。
/// </summary>
internal sealed class ExperimentalTeleportTask : IDisposable
{
    private const int TeleportTimeoutMilliseconds = 60_000;
    private const int RetryCount = 3;
    private const int MaximumFastDragIterations = 12;
    private const double FinalZoomLevelLimit = 2d;
    private const double DefaultDisplayZoomLevel = 4.4d;
    private const double MoonCanonDisplayZoomLevel = 3d;
    private const double AdjacentPointDistanceFactor = 30d;
    private const double SpecialPointTolerance = 50d;

    private static readonly SpecialAdjacentPoint[] BuiltinSpecialAdjacentPoints =
    [
        new(-796.32, 1037.22),
        new(1184.09, 622.63),
        new(3383.84, 2692.99),
        new(-2469.92, 4300.78),
        new(3887.81, 1235.82),
        new(2777.39, 1525.53),
        new(9605.335, -1852.241),
        new(828.93, -582.76),
        new(522.56, 528.84),
        new(117.9796, 2651.545),
        new(131.9534, 2651.928),
        new(-825.87, 1039.45),
        new(-4378.864, -2501.427),
        new(-4404.72, -2485.26),
        new(9691.779, -1624.362),
        new(9709.071, -1649.9),
        new(9724.226, 5445.849),
        new(9715.974, 5480.602),
        new(9643.603, -1857.9),
    ];

    private static IReadOnlyList<SpecialAdjacentPoint>? s_userSpecialAdjacentPoints;
    private static Point2f? s_lastTarget;
    private static string? s_lastMapName;

    private readonly CancellationToken _ct;
    private readonly TpConfig _config;
    private readonly TpTask _host;
    private readonly ExperimentalTeleportDrag _drag;
    private readonly ExperimentalTeleportRegionAtlas _regionAtlas;
    private readonly ExperimentalTeleportUiStateMachine _uiStateMachine;

    private ExperimentalTeleportTask(CancellationToken ct)
    {
        _ct = ct;
        _config = TaskContext.Instance().Config.TpConfig;
        _host = new TpTask(ct);
        _drag = new ExperimentalTeleportDrag(_config, ct);
        _regionAtlas = new ExperimentalTeleportRegionAtlas(_config);
        _uiStateMachine = new ExperimentalTeleportUiStateMachine(_host, _regionAtlas, _config, ct);
    }

    public static async Task<(double, double)> Run(
        CancellationToken cancellationToken,
        double tpX,
        double tpY,
        string mapName,
        bool force)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TeleportTimeoutMilliseconds);
        using var task = new ExperimentalTeleportTask(timeoutCts.Token);
        try
        {
            return await task.RunWithRetries(tpX, tpY, mapName, force);
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"实验传送超过 {TeleportTimeoutMilliseconds / 1000} 秒",
                ex);
        }
    }

    private async Task<(double, double)> RunWithRetries(
        double tpX,
        double tpY,
        string mapName,
        bool force)
    {
        const int retryCount = RetryCount;
        for (var attempt = 1; attempt <= retryCount; attempt++)
        {
            try
            {
                return await RunOnce(tpX, tpY, mapName, force);
            }
            catch (TeleportTargetLocalizationException ex)
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
                Logger.LogWarning(
                    "实验传送第 {Attempt}/{RetryCount} 次点选定位失败，保持大地图并重试：{Message}",
                    attempt,
                    retryCount,
                    ex.Message);
                await Delay(GetOperationDelay(300), _ct);
            }
            catch (TpPointNotActivate ex)
            {
                await Delay(GetOperationDelay(300), _ct);
                Logger.LogWarning("{Message}  重试", ex.Message);
            }
            catch (Exception ex) when (ex is NormalEndException or OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    "实验传送第 {Attempt}/{RetryCount} 次失败，原因：{Message}",
                    attempt,
                    retryCount,
                    ex.Message);
                LogDetailed(ex, "实验传送失败异常详情（第 {Attempt}/{RetryCount} 次）", attempt, retryCount);
                Simulation.SendInput.Mouse.LeftButtonUp();
                await new ReturnMainUiTask().Start(_ct);
                await Delay(GetOperationDelay(1000), _ct);
            }
        }

        throw new InvalidOperationException("实验传送失败");
    }

    private async Task<(double, double)> RunOnce(
        double tpX,
        double tpY,
        string mapName,
        bool force)
    {
        var navigationPrior = GetNavigationPrior(mapName);
        await _uiStateMachine.EnsureMapMainAsync(mapName);
        await _host.SwitchToGroundMapLayerIfNeeded();

        var target = ResolveTarget(tpX, tpY, mapName, force);
        LogDetailed(
            "实验传送目标：map={MapName} request=({RequestX:0.0},{RequestY:0.0}) target=({TargetX:0.0},{TargetY:0.0}) country={Country} neighbor={NeighborDistance:0.0} force={Force}",
            mapName,
            tpX,
            tpY,
            target.X,
            target.Y,
            target.Country ?? "未指定",
            target.NeighborDistance,
            force);
        var switchedCenter = await SwitchTargetArea(target, navigationPrior);
        await _drag.WaitForStableMapAsync();

        var initialPrior = switchedCenter ?? navigationPrior ?? GetLastTargetPrior(mapName);
        await MoveTargetIntoClickableArea(target, initialPrior);

        var finalZoomLevel = GetFinalZoomLevel(target);
        LogDetailed(
            "实验传送进入最终点选：map={MapName} target=({TargetX:0.0},{TargetY:0.0}) zoom={ZoomLevel:0.00}",
            mapName,
            target.X,
            target.Y,
            finalZoomLevel);
        var result = await _host.CompleteExperimentalTeleport(
            tpX,
            tpY,
            mapName,
            force,
            finalZoomLevel,
            _drag.AdjustMapZoomLevelAsync,
            _uiStateMachine);
        s_lastTarget = new Point2f((float)result.Item1, (float)result.Item2);
        s_lastMapName = mapName;
        return result;
    }

    private TeleportTarget ResolveTarget(double tpX, double tpY, string mapName, bool force)
    {
        var nearest = _host.GetNearestNTpPoints(tpX, tpY, mapName, 2);
        if (nearest.Count == 0)
        {
            throw new InvalidOperationException($"地图 {mapName} 中没有传送点数据");
        }

        var targetPoint = nearest[0];
        var neighborPoint = nearest.Count > 1 ? nearest[1] : targetPoint;
        var targetX = force ? tpX : targetPoint.X;
        var targetY = force ? tpY : targetPoint.Y;
        var neighborDistance = Math.Sqrt(
            Math.Pow(targetPoint.X - neighborPoint.X, 2) +
            Math.Pow(targetPoint.Y - neighborPoint.Y, 2));
        return new TeleportTarget(
            targetX,
            targetY,
            mapName,
            targetPoint.Country,
            neighborDistance);
    }

    private async Task<Point2f?> SwitchTargetArea(
        TeleportTarget target,
        Point2f? navigationPrior)
    {
        if (target.MapName == MapTypes.Teyvat.ToString())
        {
            var currentCenter = TryRecognizeCenter(target.MapName, navigationPrior ?? GetLastTargetPrior(target.MapName));
            if (currentCenter is { } center && !ShouldSwitchCountry(target, center))
            {
                LogDetailed(
                    "实验传送沿用当前地区：country={Country} center=({CenterX:0.0},{CenterY:0.0})",
                    target.Country ?? "未指定",
                    center.X,
                    center.Y);
                return center;
            }

            if (string.IsNullOrWhiteSpace(target.Country))
            {
                LogDetailed("实验传送目标没有国家信息，跳过地区切换");
                return currentCenter;
            }

            LogDetailed(
                "实验传送切换地区：country={Country} currentCenter={CurrentCenter}",
                target.Country,
                currentCenter?.ToString() ?? "未识别");
            await SwitchAreaWithFallback(target.Country, target.MapName);
            return GetCountryCenter(target.Country);
        }

        if (string.Equals(s_lastMapName, target.MapName, StringComparison.Ordinal))
        {
            LogDetailed("实验传送沿用独立地图：map={MapName}", target.MapName);
            return GetLastTargetPrior(target.MapName);
        }

        var areaName = MapTypesExtensions.ParseFromName(target.MapName).GetDescription();
        LogDetailed("实验传送切换独立地图：map={MapName} area={Area}", target.MapName, areaName);
        await SwitchAreaWithFallback(areaName, target.MapName);
        return null;
    }

    private async Task SwitchAreaWithFallback(string areaName, string mapName)
    {
        await _uiStateMachine.SwitchAreaAsync(areaName, mapName);
    }

    private async Task MoveTargetIntoClickableArea(TeleportTarget target, Point2f? initialPrior)
    {
        var center = TryRecognizeCenter(target.MapName, initialPrior);
        if (center == null)
        {
            LogDetailed("实验传送未取得初始地图中心，将交由主线定位兜底");
            return;
        }

        var currentCenter = center.Value;
        var currentZoom = _host.GetCurrentBigMapZoomLevel();
        for (var iteration = 0;
             iteration < Math.Min(_config.MaxIterations, MaximumFastDragIterations);
             iteration++)
        {
            _ct.ThrowIfCancellationRequested();
            if (_drag.IsTargetSafelyClickable(
                    target.X,
                    target.Y,
                    currentCenter,
                    currentZoom,
                    target.Country))
            {
                LogDetailed(
                    "实验传送目标已进入安全点击区：iteration={Iteration} center=({CenterX:0.0},{CenterY:0.0}) zoom={Zoom:0.00}",
                    iteration,
                    currentCenter.X,
                    currentCenter.Y,
                    currentZoom);
                return;
            }

            var deltaX = _config.MapScaleFactor * (target.X - currentCenter.X) / currentZoom;
            var deltaY = _config.MapScaleFactor * (target.Y - currentCenter.Y) / currentZoom;
            var mouseDistance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            LogDetailed(
                "实验传送定位轮次：iteration={Iteration} center=({CenterX:0.0},{CenterY:0.0}) target=({TargetX:0.0},{TargetY:0.0}) zoom={Zoom:0.00} delta=({DeltaX:0.0},{DeltaY:0.0}) distance={Distance:0.0}",
                iteration + 1,
                currentCenter.X,
                currentCenter.Y,
                target.X,
                target.Y,
                currentZoom,
                deltaX,
                deltaY,
                mouseDistance);
            if (_config.MapZoomEnabled && mouseDistance > _config.MapZoomOutDistance)
            {
                var targetZoom = Math.Min(
                    _config.MaxZoomLevel,
                    currentZoom * mouseDistance / _config.MapZoomOutDistance);
                if (targetZoom > currentZoom + _config.PrecisionThreshold)
                {
                    var previousZoom = currentZoom;
                    await _drag.AdjustMapZoomLevelAsync(currentZoom, targetZoom);
                    await _drag.WaitForStableMapAsync();
                    currentZoom = _host.GetCurrentBigMapZoomLevel();
                    LogDetailed(
                        "实验传送缩放完成：iteration={Iteration} requested={RequestedZoom:0.00} before={BeforeZoom:0.00} actual={ActualZoom:0.00}",
                        iteration + 1,
                        targetZoom,
                        previousZoom,
                        currentZoom);
                    deltaX = _config.MapScaleFactor * (target.X - currentCenter.X) / currentZoom;
                    deltaY = _config.MapScaleFactor * (target.Y - currentCenter.Y) / currentZoom;
                }
            }

            var dragResult = await _drag.DragAsync(deltaX, deltaY, target.Country);
            if (!dragResult.Moved)
            {
                LogDetailed("实验传送未找到可用拖动跑道，将交由主线定位兜底");
                return;
            }

            await _drag.WaitForStableMapAsync();
            var predictedCenter = new Point2f(
                (float)(currentCenter.X + dragResult.CursorDeltaX * currentZoom / _config.MapScaleFactor),
                (float)(currentCenter.Y + dragResult.CursorDeltaY * currentZoom / _config.MapScaleFactor));
            var recognizedCenter = TryRecognizeCenter(target.MapName, predictedCenter);
            if (recognizedCenter == null)
            {
                LogDetailed("实验传送拖动后位置识别失败，将交由主线定位兜底");
                return;
            }

            var actualMapScreenX =
                (recognizedCenter.Value.X - currentCenter.X) * _config.MapScaleFactor / currentZoom;
            var actualMapScreenY =
                (recognizedCenter.Value.Y - currentCenter.Y) * _config.MapScaleFactor / currentZoom;
            _drag.UpdateRelativeMoveMultiplier(
                dragResult.InputDeltaX,
                dragResult.InputDeltaY,
                actualMapScreenX,
                actualMapScreenY);

            LogDetailed(
                "实验传送拖动定位完成：iteration={Iteration} input=({InputX:0.0},{InputY:0.0}) cursor=({CursorX:0.0},{CursorY:0.0}) map=({MapX:0.0},{MapY:0.0}) predicted=({PredictedX:0.0},{PredictedY:0.0}) recognized=({RecognizedX:0.0},{RecognizedY:0.0})",
                iteration + 1,
                dragResult.InputDeltaX,
                dragResult.InputDeltaY,
                dragResult.CursorDeltaX,
                dragResult.CursorDeltaY,
                actualMapScreenX,
                actualMapScreenY,
                predictedCenter.X,
                predictedCenter.Y,
                recognizedCenter.Value.X,
                recognizedCenter.Value.Y);
            currentCenter = recognizedCenter.Value;
            currentZoom = _host.GetCurrentBigMapZoomLevel();
        }

        Logger.LogWarning(
            "实验传送快速定位达到轮次上限，将交由主线定位兜底：maxIterations={MaxIterations}",
            Math.Min(_config.MaxIterations, MaximumFastDragIterations));
    }

    private Point2f? TryRecognizeCenter(string mapName, Point2f? prior)
    {
        try
        {
            return _host.GetBigMapCenterPoint(mapName, prior);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDetailed(ex, "实验传送识别大地图中心失败：{MapName}", mapName);
            return null;
        }
    }

    private static bool ShouldSwitchCountry(TeleportTarget target, Point2f currentCenter)
    {
        if (string.IsNullOrWhiteSpace(target.Country))
        {
            return false;
        }

        var countryCenter = GetCountryCenter(target.Country);
        if (countryCenter == null)
        {
            return true;
        }

        return Distance(currentCenter, target.X, target.Y) >
               Distance(countryCenter.Value, target.X, target.Y);
    }

    private static Point2f? GetCountryCenter(string country)
    {
        if (!MapLazyAssets.Get().CountryPositions.TryGetValue(country, out var position) || position.Length < 2)
        {
            return null;
        }

        return new Point2f((float)position[0], (float)position[1]);
    }

    private static Point2f? GetNavigationPrior(string mapName)
    {
        if (mapName != MapTypes.Teyvat.ToString())
        {
            return null;
        }

        var (x, y) = Navigation.GetTeleportPositionPrior();
        return float.IsFinite(x) && float.IsFinite(y) && (x != -1f || y != -1f)
            ? new Point2f(x, y)
            : null;
    }

    private static Point2f? GetLastTargetPrior(string mapName)
    {
        return string.Equals(s_lastMapName, mapName, StringComparison.Ordinal)
            ? s_lastTarget
            : null;
    }

    private double GetFinalZoomLevel(TeleportTarget target)
    {
        var zoom = Math.Max(target.NeighborDistance / AdjacentPointDistanceFactor, 1d);
        zoom = Math.Min(zoom, FinalZoomLevelLimit);

        var special = GetSpecialAdjacentPoints().FirstOrDefault(point =>
            Math.Abs(point.X - target.X) <= SpecialPointTolerance &&
            Math.Abs(point.Y - target.Y) <= SpecialPointTolerance);
        if (special != null)
        {
            zoom = Math.Min(zoom, special.Zoom ?? 1.5d);
        }

        if (target.MapName == MapTypes.SeaOfBygoneEras.ToString())
        {
            zoom = Math.Max(zoom, 2d);
        }

        var displayLimit = target.MapName == MapTypes.MoonCanon.ToString()
            ? MoonCanonDisplayZoomLevel
            : DefaultDisplayZoomLevel;
        return Math.Clamp(zoom, 1d, displayLimit);
    }

    private static IReadOnlyList<SpecialAdjacentPoint> GetSpecialAdjacentPoints()
    {
        if (s_userSpecialAdjacentPoints == null)
        {
            s_userSpecialAdjacentPoints = LoadUserSpecialAdjacentPoints();
        }

        return s_userSpecialAdjacentPoints.Count == 0
            ? BuiltinSpecialAdjacentPoints
            : BuiltinSpecialAdjacentPoints.Concat(s_userSpecialAdjacentPoints).ToArray();
    }

    private static IReadOnlyList<SpecialAdjacentPoint> LoadUserSpecialAdjacentPoints()
    {
        var path = Global.Absolute(@"User\AutoTrackPath\special_adjacent_tp_points.json");
        if (!File.Exists(path))
        {
            return Array.Empty<SpecialAdjacentPoint>();
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<SpecialAdjacentPoint>>(File.ReadAllText(path), options)
                   ?? [];
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "用户特殊相邻传送点配置读取失败，将仅使用内置数据");
            return Array.Empty<SpecialAdjacentPoint>();
        }
    }

    private int GetOperationDelay(int baseDelay)
    {
        var configured = Math.Clamp(
            _config.TeleportOperationDelayMilliseconds,
            TpConfig.MinTeleportOperationDelayMilliseconds,
            TpConfig.MaxTeleportOperationDelayMilliseconds);
        return Math.Max(1, (int)Math.Round(
            baseDelay * configured / (double)TpConfig.DefaultTeleportOperationDelayMilliseconds));
    }

    private void LogDetailed(string message, params object?[] args)
    {
        if (_config.ExperimentalTeleportDetailedLogs)
        {
            Logger.LogDebug(message, args);
        }
    }

    private void LogDetailed(Exception exception, string message, params object?[] args)
    {
        if (_config.ExperimentalTeleportDetailedLogs)
        {
            Logger.LogDebug(exception, message, args);
        }
    }

    private static double Distance(Point2f point, double x, double y)
    {
        return Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));
    }

    public void Dispose()
    {
        _regionAtlas.Dispose();
    }

    private sealed class SpecialAdjacentPoint
    {
        public SpecialAdjacentPoint()
        {
        }

        public SpecialAdjacentPoint(double x, double y, double? zoom = null)
        {
            X = x;
            Y = y;
            Zoom = zoom;
        }

        public double X { get; set; }
        public double Y { get; set; }
        public double? Zoom { get; set; }
    }

    private readonly record struct TeleportTarget(
        double X,
        double Y,
        string MapName,
        string? Country,
        double NeighborDistance);

}
