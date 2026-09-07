using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 实验传送专用的地图拖动与缩放滑块操作。
/// </summary>
internal sealed class ExperimentalTeleportDrag(TpConfig config, CancellationToken ct)
{
    private const double SafeMargin = 50d;
    internal const double EarlyStopMargin = 40d;
    private const double ZoomButtonX = 47d;
    private const double ZoomStartY = 468d;
    private const double ZoomEndY = 612d;
    private const int MaxStartValidationAttempts = 5;
    private const int StartCandidateRandomTopPercent = 30;
    // 模板匹配区域需要覆盖图标完整尺寸；边缘保留约 65px，实际起点只在内部 270×270 内生成。
    private const double StartProbeRegionSize1080P = 400d;
    private const double StartProbeSelectableRegionSize1080P = 270d;

    private static readonly Rect2d[] DangerRects =
    [
        new(0, 0, 400, 430),
        new(0, 430, 110, 260),
        new(930, 0, 990, 100),
        new(1515, 929, 405, 151),
        new(0, 960, 105, 120),
        new(1780, 350, 140, 375),
        new(0, 0, 1920, 20),
    ];

    private static readonly Rect2d SnezhnayaDangerRect = new(797, 984, 330, 96);

    internal readonly record struct DragResult(
        double InputDeltaX,
        double InputDeltaY,
        double CursorDeltaX,
        double CursorDeltaY,
        double StartX,
        double StartY)
    {
        public bool Moved => Math.Abs(CursorDeltaX) + Math.Abs(CursorDeltaY) >= 2d;
    }

    private readonly record struct StartCandidate(
        Point2d Point,
        double BoundaryDistance,
        double Length,
        bool CanComplete,
        double SafetySlack,
        int Order);

    public bool IsTargetSafelyClickable(
        double targetX,
        double targetY,
        Point2f center,
        double zoomLevel,
        string? country)
    {
        if (zoomLevel <= 0)
        {
            return false;
        }

        var rect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var clickX = rect.Width / 2d - config.MapScaleFactor * (targetX - center.X) / zoomLevel;
        var clickY = rect.Height / 2d - config.MapScaleFactor * (targetY - center.Y) / zoomLevel;
        return IsSafePoint(clickX, clickY, rect.Width, rect.Height, EarlyStopMargin, country);
    }

    internal bool CanCompleteSingleDrag(
        double requestedDeltaX,
        double requestedDeltaY,
        string? country = null)
    {
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var correction = double.IsFinite(config.ExperimentalTeleportDragDistanceCorrection) &&
                         config.ExperimentalTeleportDragDistanceCorrection > 0
            ? config.ExperimentalTeleportDragDistanceCorrection
            : TpConfig.DefaultExperimentalTeleportDragDistanceCorrection;
        var desiredX = requestedDeltaX * correction;
        var desiredY = requestedDeltaY * correction;
        var desiredDistance = Math.Sqrt(desiredX * desiredX + desiredY * desiredY);
        if (!double.IsFinite(desiredDistance))
        {
            return false;
        }

        if (!TryCreateRelaxedRunway(
                desiredX,
                desiredY,
                captureRect.Width,
                captureRect.Height,
                country,
                null,
                attemptedStartCandidates: null,
                out var start,
                out var end))
        {
            return false;
        }

        var runwayDistance = Math.Sqrt(
            Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        return runwayDistance + 1e-6d >= desiredDistance;
    }

    public async Task<DragResult> DragAsync(
        double requestedDeltaX,
        double requestedDeltaY,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects = null,
        Func<Rect2d, IReadOnlyList<Rect2d>>? forbiddenStartProbe = null,
        ISet<(int X, int Y)>? attemptedStartCandidates = null)
    {
        // 目标已经到位时无需按下鼠标，避免产生无效拖动并触发上层重复识别。
        if (requestedDeltaX == 0d && requestedDeltaY == 0d)
        {
            return default;
        }

        var systemInfo = TaskContext.Instance().SystemInfo;
        var captureRect = systemInfo.ScaleMax1080PCaptureRect;
        var realCaptureRect = systemInfo.CaptureAreaRect;
        var distanceCorrection = double.IsFinite(config.ExperimentalTeleportDragDistanceCorrection) &&
                                  config.ExperimentalTeleportDragDistanceCorrection > 0
            ? config.ExperimentalTeleportDragDistanceCorrection
            : TpConfig.DefaultExperimentalTeleportDragDistanceCorrection;
        var effectiveDistanceCorrection = distanceCorrection;
        var desiredX = requestedDeltaX * effectiveDistanceCorrection;
        var desiredY = requestedDeltaY * effectiveDistanceCorrection;
        var activeForbiddenStartRects = forbiddenStartRects?.ToList() ?? [];
        Point2d selectedStart = default;
        Point2d selectedEnd = default;
        var startValidationAttempts = 0;
        var probedStartCandidates = new HashSet<(int X, int Y)>();
        while (true)
        {
            var rankedAreaCenters = GetOrderedRelaxedStartCandidates(
                desiredX,
                desiredY,
                captureRect.Width,
                captureRect.Height,
                country,
                activeForbiddenStartRects,
                attemptedStartCandidates);
            if (rankedAreaCenters.Count == 0)
            {
                LogDetailed(
                    "实验传送无法生成安全拖动跑道：requested=({RequestedX:0.0},{RequestedY:0.0}) adjusted=({AdjustedX:0.0},{AdjustedY:0.0}) country={Country}",
                    requestedDeltaX,
                    requestedDeltaY,
                    desiredX,
                    desiredY,
                    country ?? "未指定");
                return default;
            }

            var poolSize = Math.Max(1, (int)Math.Ceiling(
                rankedAreaCenters.Count * StartCandidateRandomTopPercent / 100d));
            var areaPool = rankedAreaCenters.Take(poolSize).ToList();
            var unprobedAreas = areaPool
                .Where(candidate => !probedStartCandidates.Contains(GetStartCandidateKey(candidate.Point)))
                .ToList();
            if (unprobedAreas.Count == 0)
            {
                unprobedAreas = rankedAreaCenters
                    .Where(candidate => !probedStartCandidates.Contains(GetStartCandidateKey(candidate.Point)))
                    .ToList();
            }

            if (unprobedAreas.Count == 0)
            {
                LogDetailed("实验传送起点候选区域均已匹配，停止重复探测");
                return default;
            }

            var areaCenter = unprobedAreas[Random.Shared.Next(unprobedAreas.Count)];
            var areaRect = CreateStartProbeRegion(
                areaCenter.Point,
                captureRect.Width,
                captureRect.Height,
                StartProbeRegionSize1080P);
            var selectableRect = CreateStartProbeRegion(
                areaCenter.Point,
                captureRect.Width,
                captureRect.Height,
                StartProbeSelectableRegionSize1080P);
            var areaKey = GetStartCandidateKey(areaCenter.Point);
            probedStartCandidates.Add(areaKey);

            if (forbiddenStartProbe is not null)
            {
                var discoveredForbiddenRects = forbiddenStartProbe(areaRect);
                if (discoveredForbiddenRects is { Count: > 0 })
                {
                    activeForbiddenStartRects.AddRange(discoveredForbiddenRects);
                    startValidationAttempts++;
                    LogDetailed(
                        "实验传送起点区域匹配发现禁区，重新选点：attempt={Attempt}/{MaxAttempts} added={AddedCount} total={TotalCount}",
                        startValidationAttempts,
                        MaxStartValidationAttempts,
                        discoveredForbiddenRects.Count,
                        activeForbiddenStartRects.Count);
                }
            }

            var rankedStarts = GetOrderedRelaxedStartCandidates(
                desiredX,
                desiredY,
                captureRect.Width,
                captureRect.Height,
                country,
                activeForbiddenStartRects,
                attemptedStartCandidates,
                selectableRect);
            if (rankedStarts.Count == 0)
            {
                if (startValidationAttempts >= MaxStartValidationAttempts)
                {
                    return default;
                }

                continue;
            }

            var startPoolSize = Math.Max(1, (int)Math.Ceiling(
                rankedStarts.Count * StartCandidateRandomTopPercent / 100d));
            var startPool = rankedStarts.Take(startPoolSize).ToList();
            var selected = startPool[Random.Shared.Next(startPool.Count)];
            attemptedStartCandidates?.Add(GetStartCandidateKey(selected.Point));
            var distance = Math.Sqrt(desiredX * desiredX + desiredY * desiredY);
            var directionX = distance <= 1e-6d ? 0d : desiredX / distance;
            var directionY = distance <= 1e-6d ? 0d : desiredY / distance;
            selectedStart = selected.Point;
            selectedEnd = new Point2d(
                selected.Point.X + directionX * selected.Length,
                selected.Point.Y + directionY * selected.Length);
            break;
        }

        var start = selectedStart;
        var end = selectedEnd;

        var screenStart = ToScreenPoint(start, captureRect, realCaptureRect);
        var screenEnd = ToScreenPoint(end, captureRect, realCaptureRect, clampToCapture: true);
        var boundaryDelay = GetOperationInterval();
        var dragStartDelay = Math.Max(1, config.ExperimentalTeleportDragStartDelayMilliseconds);
        var dragReleaseDelay = Math.Max(1, config.ExperimentalTeleportDragReleaseDelayMilliseconds);

        LogDetailed(
            "实验传送开始拖动：requested=({RequestedX:0.0},{RequestedY:0.0}) " +
            "runway=({StartX:0.0},{StartY:0.0})->({EndX:0.0},{EndY:0.0}) " +
            "screenRunway=({ScreenStartX:0.0},{ScreenStartY:0.0})->({ScreenEndX:0.0},{ScreenEndY:0.0})",
            requestedDeltaX,
            requestedDeltaY,
            start.X,
            start.Y,
            end.X,
            end.Y,
            screenStart.X,
            screenStart.Y,
            screenEnd.X,
            screenEnd.Y);

        LogDetailed(
            "实验传送拖动起点避让：forbiddenCount={ForbiddenCount} start=({StartX:0.0},{StartY:0.0})",
            activeForbiddenStartRects.Count,
            start.X,
            start.Y);

        MoveToCapturePoint(start, captureRect, realCaptureRect);
        await Delay(boundaryDelay, ct);
        GetCursorPosition(out var cursorBefore);

        var inputDistance = Math.Sqrt(
            Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        var requestedDistance = Math.Sqrt(
            requestedDeltaX * requestedDeltaX + requestedDeltaY * requestedDeltaY);
        var desiredDistance = Math.Sqrt(desiredX * desiredX + desiredY * desiredY);
        var runwayRatio = desiredDistance <= 1e-6d ? 0d : inputDistance / desiredDistance;
        var maxSingleStepDistance = Math.Max(1, config.ExperimentalTeleportMaxSingleStepDistancePixels);
        var steps = Math.Clamp(
            (int)Math.Ceiling(inputDistance / maxSingleStepDistance),
            1,
            int.MaxValue);
        var movedX = 0d;
        var movedY = 0d;
        var stepDelay = GetStepInterval();
        LogDetailed(
            "实验传送拖动参数：theory=({TheoryX:0.0},{TheoryY:0.0}) theoryDistance={TheoryDistance:0.0} " +
            "distanceCorrection={DistanceCorrection:0.000} desiredInput=({DesiredX:0.0},{DesiredY:0.0}) " +
            "desiredDistance={DesiredDistance:0.0} runwayDistance={RunwayDistance:0.0} runwayRatio={RunwayRatio:0.000} " +
            "runwayDelta=({RunwayDeltaX:0.0},{RunwayDeltaY:0.0}) " +
            "maxStepDistance={MaxStepDistance:0.0} steps={Steps} stepDelay={StepDelay}ms " +
            "boundaryDelay={BoundaryDelay}ms dragStartDelay={DragStartDelay}ms dragReleaseDelay={DragReleaseDelay}ms",
            requestedDeltaX,
            requestedDeltaY,
            requestedDistance,
            effectiveDistanceCorrection,
            desiredX,
            desiredY,
            desiredDistance,
            inputDistance,
            runwayRatio,
            end.X - start.X,
            end.Y - start.Y,
            maxSingleStepDistance,
            steps,
            stepDelay,
            boundaryDelay,
            dragStartDelay,
            dragReleaseDelay);
        var dragStartedAt = Environment.TickCount64;
        try
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(dragStartDelay, ct);
            for (var i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                var progress = i / (double)steps;
                var nextX = (end.X - start.X) * progress;
                var nextY = (end.Y - start.Y) * progress;
                movedX = nextX;
                movedY = nextY;

                MoveToCapturePoint(
                    new Point2d(start.X + movedX, start.Y + movedY),
                    captureRect,
                    realCaptureRect);

                await Delay(i < steps ? stepDelay : dragReleaseDelay, ct);
            }
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        await Delay(boundaryDelay, ct);
        GetCursorPosition(out var cursorAfter);
        var actualX = (cursorAfter.X - cursorBefore.X) * captureRect.Width / Math.Max(1d, realCaptureRect.Width);
        var actualY = (cursorAfter.Y - cursorBefore.Y) * captureRect.Height / Math.Max(1d, realCaptureRect.Height);
        var actualCursorDistance = Math.Sqrt(actualX * actualX + actualY * actualY);
        var plannedInputDistance = inputDistance;
        var inputCompletionRatio = plannedInputDistance <= 1e-6d
            ? 0d
            : actualCursorDistance / plannedInputDistance;
        LogDetailed(
            "实验传送拖动完成：input=({InputX:0.0},{InputY:0.0}) plannedDistance={PlannedDistance:0.0} " +
            "cursor=({CursorX:0.0},{CursorY:0.0}) actualDistance={ActualDistance:0.0} completionRatio={CompletionRatio:0.000} " +
            "multiplier={Multiplier:0.000} elapsed={ElapsedMilliseconds}ms",
            end.X - start.X,
            end.Y - start.Y,
            plannedInputDistance,
            actualX,
            actualY,
            actualCursorDistance,
            inputCompletionRatio,
            effectiveDistanceCorrection,
            Environment.TickCount64 - dragStartedAt);
        return new DragResult(end.X - start.X, end.Y - start.Y, actualX, actualY, start.X, start.Y);
    }

    public async Task AdjustMapZoomLevelAsync(double zoomLevel, double targetZoomLevel)
    {
        zoomLevel = Math.Clamp(zoomLevel, 1d, 6d);
        targetZoomLevel = Math.Clamp(targetZoomLevel, 1d, 6d);
        if (Math.Abs(zoomLevel - targetZoomLevel) <= config.PrecisionThreshold)
        {
            return;
        }

        var initialY = ZoomStartY + (ZoomEndY - ZoomStartY) * (zoomLevel - 1d) / 5d;
        var targetY = ZoomStartY + (ZoomEndY - ZoomStartY) * (targetZoomLevel - 1d) / 5d;
        var buttonX = ZoomButtonX + 10d;
        var realRect = SystemControl.GetCaptureRect(TaskContext.Instance().GameHandle);
        var realScale = Math.Max(1e-6d, realRect.Width / 1920d);

        LogDetailed(
            "实验传送滑块缩放：before={BeforeZoom:0.00} target={TargetZoom:0.00} from=({StartX:0.0},{InitialY:0.0}) to=({TargetX:0.0},{TargetY:0.0}) scale={Scale:0.000}",
            zoomLevel,
            targetZoomLevel,
            buttonX,
            initialY,
            buttonX,
            targetY,
            realScale);

        DesktopRegion.DesktopRegionMove(
            realRect.X + buttonX * realScale,
            realRect.Y + initialY * realScale);
        await Delay(GetOperationInterval(), ct);
        var dragStartDelay = Math.Max(1, config.ExperimentalTeleportDragStartDelayMilliseconds);
        var dragReleaseDelay = Math.Max(1, config.ExperimentalTeleportDragReleaseDelayMilliseconds);
        try
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(dragStartDelay, ct);
            DesktopRegion.DesktopRegionMove(
                realRect.X + buttonX * realScale,
                realRect.Y + targetY * realScale);
            await Delay(dragReleaseDelay, ct);
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        await Delay(GetOperationInterval(), ct);
    }

    private static bool TryCreateRelaxedRunway(
        double requestedX,
        double requestedY,
        int width,
        int height,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        ISet<(int X, int Y)>? attemptedStartCandidates,
        out Point2d start,
        out Point2d end)
    {
        start = default;
        end = default;
        var requestedDistance = Math.Sqrt(requestedX * requestedX + requestedY * requestedY);
        if (!double.IsFinite(requestedDistance) || width <= 0 || height <= 0)
        {
            start = default;
            end = default;
            return false;
        }

        var rankedCandidates = GetOrderedRelaxedStartCandidates(
            requestedX,
            requestedY,
            width,
            height,
            country,
            forbiddenStartRects,
            attemptedStartCandidates);
        if (rankedCandidates.Count == 0)
        {
            return false;
        }

        var selected = rankedCandidates[0];
        var selectedKey = GetStartCandidateKey(selected.Point);
        attemptedStartCandidates?.Add(selectedKey);
        start = selected.Point;
        var directionX = requestedDistance <= 1e-6d ? 0d : requestedX / requestedDistance;
        var directionY = requestedDistance <= 1e-6d ? 0d : requestedY / requestedDistance;
        end = new Point2d(
            selected.Point.X + directionX * selected.Length,
            selected.Point.Y + directionY * selected.Length);
        return true;
    }

    private static List<StartCandidate> GetOrderedRelaxedStartCandidates(
        double requestedX,
        double requestedY,
        int width,
        int height,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        ISet<(int X, int Y)>? attemptedStartCandidates,
        Rect2d? candidateRegion = null)
    {
        var requestedDistance = Math.Sqrt(requestedX * requestedX + requestedY * requestedY);
        if (!double.IsFinite(requestedDistance) || width <= 0 || height <= 0)
        {
            return [];
        }

        var directionX = requestedDistance <= 1e-6d ? 0d : requestedX / requestedDistance;
        var directionY = requestedDistance <= 1e-6d ? 0d : requestedY / requestedDistance;
        var rankedCandidates = new List<StartCandidate>();
        var order = 0;
        foreach (var candidate in GetRelaxedStartCandidates(width, height, requestedX, requestedY, country, forbiddenStartRects, candidateRegion))
        {
            if (!IsSafePoint(candidate.X, candidate.Y, width, height, SafeMargin, country) ||
                IsForbiddenStartPoint(candidate, forbiddenStartRects, width, height))
            {
                continue;
            }

            var candidateKey = GetStartCandidateKey(candidate);
            if (attemptedStartCandidates is not null && attemptedStartCandidates.Contains(candidateKey))
            {
                continue;
            }

            var boundaryDistance = GetScreenBoundaryDistance(candidate, directionX, directionY, width, height);
            var length = Math.Min(requestedDistance, boundaryDistance);
            if (!double.IsFinite(length) || length < 0d)
            {
                continue;
            }

            rankedCandidates.Add(new StartCandidate(
                candidate,
                boundaryDistance,
                length,
                boundaryDistance + 1e-6d >= requestedDistance,
                boundaryDistance - requestedDistance,
                order++));
        }

        var hasCompletingCandidate = rankedCandidates.Any(candidate => candidate.CanComplete);
        return hasCompletingCandidate
            ? rankedCandidates
                .Where(candidate => candidate.CanComplete)
                .OrderByDescending(candidate => candidate.SafetySlack)
                .ThenBy(candidate => candidate.Order)
                .ToList()
            : rankedCandidates
                .OrderByDescending(candidate => candidate.BoundaryDistance)
                .ThenBy(candidate => candidate.Order)
                .ToList();
    }

    private static (int X, int Y) GetStartCandidateKey(Point2d point)
    {
        return ((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private static IReadOnlyList<Point2d> GetRelaxedStartCandidates(
        int width,
        int height,
        double requestedX,
        double requestedY,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        Rect2d? candidateRegion = null)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        var minX = SafeMargin * scaleX;
        var maxX = width - SafeMargin * scaleX;
        var minY = SafeMargin * scaleY;
        var maxY = height - SafeMargin * scaleY;
        var xValues = candidateRegion is { } region
            ? CreateRegionSamples(region.X, region.Right)
            : new List<double> { minX, minX + 1d, width * 0.25d, width * 0.5d, width * 0.75d, maxX - 1d, maxX };
        var yValues = candidateRegion is { } regionY
            ? CreateRegionSamples(regionY.Y, regionY.Bottom)
            : new List<double> { minY, minY + 1d, height * 0.25d, height * 0.5d, height * 0.75d, maxY - 1d, maxY };

        if (candidateRegion is { } selectedRegion)
        {
            minX = Math.Max(minX, selectedRegion.X);
            maxX = Math.Min(maxX, selectedRegion.Right);
            minY = Math.Max(minY, selectedRegion.Y);
            maxY = Math.Min(maxY, selectedRegion.Bottom);
        }

        void AddExcludedRectBoundaries(Rect2d rect, double margin)
        {
            xValues.Add((rect.X - margin) * scaleX - 1d);
            xValues.Add((rect.Right + margin) * scaleX + 1d);
            yValues.Add((rect.Y - margin) * scaleY - 1d);
            yValues.Add((rect.Bottom + margin) * scaleY + 1d);
        }

        foreach (var danger in DangerRects)
        {
            AddExcludedRectBoundaries(danger, SafeMargin);
        }

        if (string.Equals(country, "至冬", StringComparison.Ordinal))
        {
            AddExcludedRectBoundaries(SnezhnayaDangerRect, SafeMargin);
        }

        if (forbiddenStartRects is not null)
        {
            foreach (var forbidden in forbiddenStartRects)
            {
                AddExcludedRectBoundaries(forbidden, 0d);
            }
        }

        var candidates = new List<Point2d>(xValues.Count * yValues.Count + 4);
        foreach (var x in xValues)
        {
            foreach (var y in yValues)
            {
                candidates.Add(new Point2d(
                    Math.Clamp(x, minX, maxX),
                    Math.Clamp(y, minY, maxY)));
            }
        }

        // 目标反方向边缘点优先覆盖大位移场景；区域内选点时严格限制在内部区域。
        var distance = Math.Sqrt(requestedX * requestedX + requestedY * requestedY);
        if (candidateRegion is null && distance > 1e-6d && double.IsFinite(distance))
        {
            var directionX = requestedX / distance;
            var directionY = requestedY / distance;
            candidates.Add(new Point2d(
                directionX > 0d ? minX : directionX < 0d ? maxX : width * 0.5d,
                directionY > 0d ? minY : directionY < 0d ? maxY : height * 0.5d));
        }

        if (candidateRegion is null)
        {
            candidates.Add(new Point2d(width * 0.5d, height * 0.55d));
            candidates.Add(new Point2d(width * 0.38d, height * 0.72d));
            candidates.Add(new Point2d(width * 0.62d, height * 0.72d));
        }
        return candidates
            .GroupBy(candidate => ((int)Math.Round(candidate.X), (int)Math.Round(candidate.Y)))
            .Select(group => group.First())
            .ToList();

        static List<double> CreateRegionSamples(double left, double right)
        {
            var size = Math.Max(0d, right - left);
            return Enumerable.Range(1, 5)
                .Select(index => left + size * index / 6d)
                .ToList();
        }
    }

    private static Rect2d CreateStartProbeRegion(
        Point2d center,
        int width,
        int height,
        double size1080P)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        var sizeX = size1080P * scaleX;
        var sizeY = size1080P * scaleY;
        return new Rect2d(
            center.X - sizeX / 2d,
            center.Y - sizeY / 2d,
            sizeX,
            sizeY);
    }

    private static double GetScreenBoundaryDistance(
        Point2d start,
        double directionX,
        double directionY,
        int width,
        int height)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        var minX = SafeMargin * scaleX;
        var maxX = width - SafeMargin * scaleX;
        var minY = SafeMargin * scaleY;
        var maxY = height - SafeMargin * scaleY;
        var distance = double.PositiveInfinity;
        if (directionX > 1e-9d)
        {
            distance = Math.Min(distance, (maxX - start.X) / directionX);
        }
        else if (directionX < -1e-9d)
        {
            distance = Math.Min(distance, (minX - start.X) / directionX);
        }

        if (directionY > 1e-9d)
        {
            distance = Math.Min(distance, (maxY - start.Y) / directionY);
        }
        else if (directionY < -1e-9d)
        {
            distance = Math.Min(distance, (minY - start.Y) / directionY);
        }

        return Math.Max(0d, distance);
    }

    private static bool IsForbiddenStartPoint(
        Point2d point,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        int width,
        int height)
    {
        if (forbiddenStartRects is null || forbiddenStartRects.Count == 0)
        {
            return false;
        }

        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        return forbiddenStartRects.Any(rect =>
            point.X >= rect.X * scaleX &&
            point.X <= rect.Right * scaleX &&
            point.Y >= rect.Y * scaleY &&
            point.Y <= rect.Bottom * scaleY);
    }

    internal static bool IsSafePoint(
        double x,
        double y,
        int width,
        int height,
        double margin,
        string? country)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        if (x < margin * scaleX || x > width - margin * scaleX ||
            y < margin * scaleY || y > height - margin * scaleY)
        {
            return false;
        }

        foreach (var danger in DangerRects)
        {
            if (ContainsExpanded(danger, x, y, scaleX, scaleY, margin))
            {
                return false;
            }
        }

        return !string.Equals(country, "至冬", StringComparison.Ordinal) ||
               !ContainsExpanded(SnezhnayaDangerRect, x, y, scaleX, scaleY, margin);
    }

    private static bool ContainsExpanded(
        Rect2d rect,
        double x,
        double y,
        double scaleX,
        double scaleY,
        double margin)
    {
        return x >= (rect.X - margin) * scaleX &&
               x <= (rect.Right + margin) * scaleX &&
               y >= (rect.Y - margin) * scaleY &&
               y <= (rect.Bottom + margin) * scaleY;
    }

    private int GetOperationInterval()
    {
        var scaledDelay = 50d * config.TeleportOperationDelayMultiplier;
        return !double.IsFinite(scaledDelay) || scaledDelay >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Round(scaledDelay));
    }

    private int GetStepInterval()
    {
        return Math.Max(1, config.ExperimentalTeleportDragStepIntervalMilliseconds);
    }

    private void LogDetailed(string message, params object?[] args)
    {
        if (config.ExperimentalTeleportDetailedLogs)
        {
            Logger.LogDebug(message, args);
        }
    }

    private static void GetCursorPosition(out POINT point)
    {
        User32.GetCursorPos(out point);
    }

    private static void MoveToCapturePoint(Point2d point, Rect captureRect, RECT realCaptureRect)
    {
        var screenPoint = ToScreenPoint(point, captureRect, realCaptureRect);
        DesktopRegion.DesktopRegionMove(screenPoint.X, screenPoint.Y);
    }

    private static Point2d ToScreenPoint(
        Point2d point,
        Rect captureRect,
        RECT realCaptureRect,
        bool clampToCapture = true)
    {
        var x = clampToCapture ? Math.Clamp(point.X, 0d, captureRect.Width) : point.X;
        var y = clampToCapture ? Math.Clamp(point.Y, 0d, captureRect.Height) : point.Y;
        return new Point2d(
            realCaptureRect.X + x * realCaptureRect.Width / Math.Max(1d, captureRect.Width),
            realCaptureRect.Y + y * realCaptureRect.Height / Math.Max(1d, captureRect.Height));
    }

}
