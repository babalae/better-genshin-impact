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
    private const double EarlyStopMargin = 40d;
    private const double ZoomButtonX = 47d;
    private const double ZoomStartY = 468d;
    private const double ZoomEndY = 612d;

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

    public async Task<DragResult> DragAsync(
        double requestedDeltaX,
        double requestedDeltaY,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects = null)
    {
        // 目标已经到位时无需按下鼠标，避免产生无效拖动并触发上层重复识别。
        if (requestedDeltaX == 0d && requestedDeltaY == 0d)
        {
            return default;
        }

        var systemInfo = TaskContext.Instance().SystemInfo;
        var captureRect = systemInfo.ScaleMax1080PCaptureRect;
        var realCaptureRect = systemInfo.CaptureAreaRect;
        var distanceCorrection = double.IsFinite(config.ExperimentalTeleportDragDistanceCorrection)
            ? Math.Clamp(
                config.ExperimentalTeleportDragDistanceCorrection,
                TpConfig.MinExperimentalTeleportDragDistanceCorrection,
                TpConfig.MaxExperimentalTeleportDragDistanceCorrection)
            : TpConfig.DefaultExperimentalTeleportDragDistanceCorrection;
        const string ratioSource = "configured";
        var desiredX = requestedDeltaX * distanceCorrection;
        var desiredY = requestedDeltaY * distanceCorrection;
        Point2d selectedStart = default;
        Point2d selectedEnd = default;
        var runwayCreated = config.ExperimentalTeleportDragSafetyLevel switch
        {
            ExperimentalTeleportDragSafetyLevel.Conservative => TryCreateSafeRunway(
                desiredX,
                desiredY,
                captureRect.Width,
                captureRect.Height,
                country,
                forbiddenStartRects,
                out selectedStart,
                out selectedEnd),
            ExperimentalTeleportDragSafetyLevel.Balanced => TryCreateRelaxedRunway(
                desiredX,
                desiredY,
                captureRect.Width,
                captureRect.Height,
                country,
                forbiddenStartRects,
                allowEndOutsideScreen: false,
                out selectedStart,
                out selectedEnd),
            ExperimentalTeleportDragSafetyLevel.Overlimit => TryCreateRelaxedRunway(
                desiredX,
                desiredY,
                captureRect.Width,
                captureRect.Height,
                country,
                forbiddenStartRects,
                allowEndOutsideScreen: true,
                out selectedStart,
                out selectedEnd),
            _ => false,
        };
        var start = selectedStart;
        var end = selectedEnd;
        if (!runwayCreated)
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

        var allowEndOutsideScreen = config.ExperimentalTeleportDragSafetyLevel == ExperimentalTeleportDragSafetyLevel.Overlimit;
        var screenStart = ToScreenPoint(start, captureRect, realCaptureRect);
        var screenEnd = ToScreenPoint(end, captureRect, realCaptureRect, clampToCapture: !allowEndOutsideScreen);
        var boundaryDelay = GetOperationInterval();
        var dragStartDelay = Math.Clamp(
            config.ExperimentalTeleportDragStartDelayMilliseconds,
            TpConfig.MinExperimentalTeleportDragStartDelayMilliseconds,
            TpConfig.MaxExperimentalTeleportDragStartDelayMilliseconds);
        var dragReleaseDelay = Math.Clamp(
            config.ExperimentalTeleportDragReleaseDelayMilliseconds,
            TpConfig.MinExperimentalTeleportDragReleaseDelayMilliseconds,
            TpConfig.MaxExperimentalTeleportDragReleaseDelayMilliseconds);

        LogDetailed(
            "实验传送开始拖动：mode={Mode} requested=({RequestedX:0.0},{RequestedY:0.0}) " +
            "runway=({StartX:0.0},{StartY:0.0})->({EndX:0.0},{EndY:0.0}) " +
            "screenRunway=({ScreenStartX:0.0},{ScreenStartY:0.0})->({ScreenEndX:0.0},{ScreenEndY:0.0}) " +
            "ratio={Ratio:0.000} source={RatioSource}",
            allowEndOutsideScreen ? "relative-overlimit" : "absolute-screen",
            requestedDeltaX,
            requestedDeltaY,
            start.X,
            start.Y,
            end.X,
            end.Y,
            screenStart.X,
            screenStart.Y,
            screenEnd.X,
            screenEnd.Y,
            distanceCorrection,
            ratioSource);

        LogDetailed(
            "实验传送拖动起点避让：forbiddenCount={ForbiddenCount} start=({StartX:0.0},{StartY:0.0})",
            forbiddenStartRects?.Count ?? 0,
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
        var maxSingleStepDistance = Math.Clamp(
            config.ExperimentalTeleportMaxSingleStepDistancePixels,
            TpConfig.MinExperimentalTeleportMaxSingleStepDistancePixels,
            TpConfig.MaxExperimentalTeleportMaxSingleStepDistancePixels);
        var steps = Math.Clamp(
            (int)Math.Ceiling(inputDistance / maxSingleStepDistance),
            1,
            int.MaxValue);
        var movedX = 0d;
        var movedY = 0d;
        var movedScreenX = 0;
        var movedScreenY = 0;
        var screenScaleX = realCaptureRect.Width / Math.Max(1d, captureRect.Width);
        var screenScaleY = realCaptureRect.Height / Math.Max(1d, captureRect.Height);
        var stepDelay = GetStepInterval();
        LogDetailed(
            "实验传送拖动参数：theory=({TheoryX:0.0},{TheoryY:0.0}) theoryDistance={TheoryDistance:0.0} " +
            "distanceCorrection={DistanceCorrection:0.000} correctionSource={CorrectionSource} desiredInput=({DesiredX:0.0},{DesiredY:0.0}) " +
            "desiredDistance={DesiredDistance:0.0} runwayDistance={RunwayDistance:0.0} runwayRatio={RunwayRatio:0.000} " +
            "runwayDelta=({RunwayDeltaX:0.0},{RunwayDeltaY:0.0}) " +
            "maxStepDistance={MaxStepDistance:0.0} steps={Steps} stepDelay={StepDelay}ms " +
            "boundaryDelay={BoundaryDelay}ms dragStartDelay={DragStartDelay}ms dragReleaseDelay={DragReleaseDelay}ms",
            requestedDeltaX,
            requestedDeltaY,
            requestedDistance,
            distanceCorrection,
            ratioSource,
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

                if (allowEndOutsideScreen)
                {
                    // 超限终点可能超出桌面，使用相对输入避免绝对坐标裁剪。
                    var targetScreenX = (int)Math.Round(nextX * screenScaleX);
                    var targetScreenY = (int)Math.Round(nextY * screenScaleY);
                    Simulation.SendInput.Mouse.MoveMouseBy(
                        targetScreenX - movedScreenX,
                        targetScreenY - movedScreenY);
                    movedScreenX = targetScreenX;
                    movedScreenY = targetScreenY;
                }
                else
                {
                    MoveToCapturePoint(
                        new Point2d(start.X + movedX, start.Y + movedY),
                        captureRect,
                        realCaptureRect);
                }

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
            distanceCorrection,
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
        try
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(GetOperationInterval(), ct);
            DesktopRegion.DesktopRegionMove(
                realRect.X + buttonX * realScale,
                realRect.Y + targetY * realScale);
            await Delay(GetOperationInterval(), ct);
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        await Delay(GetOperationInterval(), ct);
    }

    private static bool TryCreateSafeRunway(
        double requestedX,
        double requestedY,
        int width,
        int height,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        out Point2d start,
        out Point2d end)
    {
        for (var ratio = 1d; ratio >= 0.18d; ratio *= 0.82d)
        {
            var deltaX = requestedX * ratio;
            var deltaY = requestedY * ratio;
            var candidates = GetRunwayCandidates(width, height, deltaX, deltaY);
            foreach (var candidate in candidates)
            {
                var candidateEnd = new Point2d(candidate.X + deltaX, candidate.Y + deltaY);
                if (IsSafeSegment(candidate, candidateEnd, width, height, country) &&
                    !IsForbiddenStartPoint(candidate, forbiddenStartRects, width, height))
                {
                    start = candidate;
                    end = candidateEnd;
                    return true;
                }
            }
        }

        start = default;
        end = default;
        return false;
    }

    private static bool TryCreateRelaxedRunway(
        double requestedX,
        double requestedY,
        int width,
        int height,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects,
        bool allowEndOutsideScreen,
        out Point2d start,
        out Point2d end)
    {
        var requestedDistance = Math.Sqrt(requestedX * requestedX + requestedY * requestedY);
        if (!double.IsFinite(requestedDistance) || width <= 0 || height <= 0)
        {
            start = default;
            end = default;
            return false;
        }

        var directionX = requestedDistance <= 1e-6d ? 0d : requestedX / requestedDistance;
        var directionY = requestedDistance <= 1e-6d ? 0d : requestedY / requestedDistance;
        var bestScore = double.NegativeInfinity;
        var bestLength = double.NegativeInfinity;
        var found = false;
        start = default;
        end = default;
        foreach (var candidate in GetRelaxedStartCandidates(
                     width,
                     height,
                     requestedX,
                     requestedY,
                     country,
                     forbiddenStartRects))
        {
            if (!IsSafePoint(candidate.X, candidate.Y, width, height, SafeMargin, country) ||
                IsForbiddenStartPoint(candidate, forbiddenStartRects, width, height))
            {
                continue;
            }

            var boundaryDistance = GetScreenBoundaryDistance(candidate, directionX, directionY, width, height);
            var length = allowEndOutsideScreen
                ? requestedDistance
                : Math.Min(requestedDistance, boundaryDistance);
            if (!double.IsFinite(length) || length < 0d)
            {
                continue;
            }

            // 先按可用边界距离选起点，保证平衡与超限模式采用一致的起点策略。
            if (boundaryDistance > bestScore + 1e-6d ||
                Math.Abs(boundaryDistance - bestScore) <= 1e-6d && length > bestLength)
            {
                bestScore = boundaryDistance;
                bestLength = length;
                start = candidate;
                end = new Point2d(
                    candidate.X + directionX * length,
                    candidate.Y + directionY * length);
                found = true;
            }
        }

        if (found)
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<Point2d> GetRelaxedStartCandidates(
        int width,
        int height,
        double requestedX,
        double requestedY,
        string? country,
        IReadOnlyList<Rect2d>? forbiddenStartRects)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        var minX = SafeMargin * scaleX;
        var maxX = width - SafeMargin * scaleX;
        var minY = SafeMargin * scaleY;
        var maxY = height - SafeMargin * scaleY;
        var xValues = new List<double>
        {
            minX,
            minX + 1d,
            width * 0.25d,
            width * 0.5d,
            width * 0.75d,
            maxX - 1d,
            maxX,
        };
        var yValues = new List<double>
        {
            minY,
            minY + 1d,
            height * 0.25d,
            height * 0.5d,
            height * 0.75d,
            maxY - 1d,
            maxY,
        };

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

        // 目标反方向边缘点优先覆盖大位移场景，其他候选仍由边界距离评分决定。
        var distance = Math.Sqrt(requestedX * requestedX + requestedY * requestedY);
        if (distance > 1e-6d && double.IsFinite(distance))
        {
            var directionX = requestedX / distance;
            var directionY = requestedY / distance;
            candidates.Add(new Point2d(
                directionX > 0d ? minX : directionX < 0d ? maxX : width * 0.5d,
                directionY > 0d ? minY : directionY < 0d ? maxY : height * 0.5d));
        }

        candidates.Add(new Point2d(width * 0.5d, height * 0.55d));
        candidates.Add(new Point2d(width * 0.38d, height * 0.72d));
        candidates.Add(new Point2d(width * 0.62d, height * 0.72d));
        return candidates;
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

    private static IReadOnlyList<Point2d> GetRunwayCandidates(int width, int height, double deltaX, double deltaY)
    {
        var scaleX = width / 1920d;
        var scaleY = height / 1080d;
        var left = SafeMargin * scaleX;
        var right = width - SafeMargin * scaleX;
        var top = SafeMargin * scaleY;
        var bottom = height - SafeMargin * scaleY;
        var preferredX = deltaX >= 0 ? left : right;
        var preferredY = deltaY >= 0 ? top : bottom;
        return
        [
            new(width * 0.5d - deltaX / 2d, height * 0.55d - deltaY / 2d),
            new(width * 0.38d, height * 0.72d),
            new(width * 0.62d, height * 0.72d),
            new(width * 0.5d, height * 0.55d),
            new(preferredX, height * 0.55d),
            new(width * 0.5d, preferredY),
            new(preferredX, preferredY),
        ];
    }

    private static bool IsSafeSegment(Point2d start, Point2d end, int width, int height, string? country)
    {
        const int samples = 20;
        for (var i = 0; i <= samples; i++)
        {
            var t = i / (double)samples;
            var x = start.X + (end.X - start.X) * t;
            var y = start.Y + (end.Y - start.Y) * t;
            if (!IsSafePoint(x, y, width, height, SafeMargin, country))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafePoint(
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
        var configuredDelay = Math.Clamp(
            config.TeleportOperationDelayMilliseconds,
            TpConfig.MinTeleportOperationDelayMilliseconds,
            TpConfig.MaxTeleportOperationDelayMilliseconds);
        var scaledDelay = 50d * configuredDelay / TpConfig.DefaultTeleportOperationDelayMilliseconds;
        return Math.Max(1, (int)Math.Round(scaledDelay));
    }

    private int GetStepInterval()
    {
        return Math.Clamp(
            config.ExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MinExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MaxExperimentalTeleportDragStepIntervalMilliseconds);
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
