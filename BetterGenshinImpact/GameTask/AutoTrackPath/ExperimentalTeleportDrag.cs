using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
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
    private const int DragBoundaryDelayMilliseconds = 50;

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
        double CursorDeltaY)
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

    public async Task<DragResult> DragAsync(double requestedDeltaX, double requestedDeltaY, string? country)
    {
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
        if (!TryCreateSafeRunway(
                desiredX,
                desiredY,
                captureRect.Width,
                captureRect.Height,
                country,
                out var start,
                out var end))
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

        var screenStart = ToScreenPoint(start, captureRect, realCaptureRect);
        var screenEnd = ToScreenPoint(end, captureRect, realCaptureRect);

        LogDetailed(
            "实验传送开始拖动：mode=absolute-screen requested=({RequestedX:0.0},{RequestedY:0.0}) " +
            "runway=({StartX:0.0},{StartY:0.0})->({EndX:0.0},{EndY:0.0}) " +
            "screenRunway=({ScreenStartX:0.0},{ScreenStartY:0.0})->({ScreenEndX:0.0},{ScreenEndY:0.0}) " +
            "ratio={Ratio:0.000} source={RatioSource}",
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

        MoveToCapturePoint(start, captureRect, realCaptureRect);
        await Delay(DragBoundaryDelayMilliseconds, ct);
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
        var stepDelay = GetStepInterval();
        var releaseDelay = DragBoundaryDelayMilliseconds;
        LogDetailed(
            "实验传送拖动参数：theory=({TheoryX:0.0},{TheoryY:0.0}) theoryDistance={TheoryDistance:0.0} " +
            "distanceCorrection={DistanceCorrection:0.000} correctionSource={CorrectionSource} desiredInput=({DesiredX:0.0},{DesiredY:0.0}) " +
            "desiredDistance={DesiredDistance:0.0} runwayDistance={RunwayDistance:0.0} runwayRatio={RunwayRatio:0.000} " +
            "runwayDelta=({RunwayDeltaX:0.0},{RunwayDeltaY:0.0}) " +
            "maxStepDistance={MaxStepDistance:0.0} steps={Steps} stepDelay={StepDelay}ms boundaryDelay={BoundaryDelay}ms",
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
            releaseDelay);
        var dragStartedAt = Environment.TickCount64;
        try
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(DragBoundaryDelayMilliseconds, ct);
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

                await Delay(i < steps ? stepDelay : releaseDelay, ct);
            }
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        await Delay(DragBoundaryDelayMilliseconds, ct);
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
        return new DragResult(end.X - start.X, end.Y - start.Y, actualX, actualY);
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
                if (IsSafeSegment(candidate, candidateEnd, width, height, country))
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
        return Math.Clamp(
            config.ExperimentalTeleportOperationIntervalMilliseconds,
            TpConfig.MinExperimentalTeleportOperationIntervalMilliseconds,
            TpConfig.MaxExperimentalTeleportOperationIntervalMilliseconds);
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

    private static Point2d ToScreenPoint(Point2d point, Rect captureRect, RECT realCaptureRect)
    {
        var x = Math.Clamp(point.X, 0d, captureRect.Width);
        var y = Math.Clamp(point.Y, 0d, captureRect.Height);
        return new Point2d(
            realCaptureRect.X + x * realCaptureRect.Width / Math.Max(1d, captureRect.Width),
            realCaptureRect.Y + y * realCaptureRect.Height / Math.Max(1d, captureRect.Height));
    }

}
