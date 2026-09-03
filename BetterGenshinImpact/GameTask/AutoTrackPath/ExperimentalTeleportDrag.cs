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
/// 实验传送专用的地图拖动与画面稳定检测。
/// </summary>
internal sealed class ExperimentalTeleportDrag(TpConfig config, CancellationToken ct)
{
    private const double SafeMargin = 50d;
    private const double EarlyStopMargin = 40d;
    private const double EmaWeight = 0.4d;
    private const double InitialDragStrengthScale = 0.2d;
    private const int StableTimeoutMilliseconds = 1200;
    private const double StableDifferenceThreshold = 1.5d;
    private const double ZoomButtonX = 47d;
    private const double ZoomStartY = 468d;
    private const double ZoomEndY = 612d;
    private double _relativeMoveMultiplier = double.NaN;
    private double _relativeMoveInitialStrength = double.NaN;

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
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var initialDragStrengthMultiplier = double.IsFinite(config.ExperimentalTeleportInitialDragStrength)
            ? Math.Clamp(
                config.ExperimentalTeleportInitialDragStrength,
                TpConfig.MinExperimentalTeleportInitialDragStrength,
                TpConfig.MaxExperimentalTeleportInitialDragStrength)
            : TpConfig.DefaultExperimentalTeleportInitialDragStrength;
        var initialDragStrength = initialDragStrengthMultiplier * InitialDragStrengthScale;
        EnsureRelativeMoveMultiplier(initialDragStrength);

        var moveRatio = config.MapDragUseRelativeMove
            ? _relativeMoveMultiplier
            : 1d;
        var ratioSource = !config.MapDragUseRelativeMove
            ? "absolute"
            : Math.Abs(_relativeMoveMultiplier - initialDragStrength) <= 1e-6d
                ? "configured"
                : "ema";
        var desiredX = requestedDeltaX * moveRatio;
        var desiredY = requestedDeltaY * moveRatio;
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

        LogDetailed(
            "实验传送开始拖动：mode={Mode} requested=({RequestedX:0.0},{RequestedY:0.0}) runway=({StartX:0.0},{StartY:0.0})->({EndX:0.0},{EndY:0.0}) ratio={Ratio:0.000} source={RatioSource}",
            config.MapDragUseRelativeMove ? "relative" : "absolute",
            requestedDeltaX,
            requestedDeltaY,
            start.X,
            start.Y,
            end.X,
            end.Y,
            moveRatio,
            ratioSource);

        GameCaptureRegion.GameRegionMove((_, scale) => (start.X * scale, start.Y * scale));
        await Delay(GetOperationDelay(40), ct);
        GetCursorPosition(out var cursorBefore);

        var steps = Math.Clamp(
            (int)Math.Ceiling(Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)) / 48d),
            5,
            36);
        var movedX = 0d;
        var movedY = 0d;
        var stepDelay = Math.Clamp(
            config.ExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MinExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MaxExperimentalTeleportDragStepIntervalMilliseconds);
        var releaseDelay = GetOperationDelay(50);
        try
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(GetOperationDelay(50), ct);
            for (var i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                var progress = EaseOut(i / (double)steps);
                var nextX = (end.X - start.X) * progress;
                var nextY = (end.Y - start.Y) * progress;
                var stepX = nextX - movedX;
                var stepY = nextY - movedY;
                movedX = nextX;
                movedY = nextY;

                if (config.MapDragUseRelativeMove)
                {
                    GameCaptureRegion.GameRegionMoveBy((_, scale) => (stepX * scale, stepY * scale));
                }
                else
                {
                    GameCaptureRegion.GameRegionMove((_, scale) =>
                        ((start.X + movedX) * scale, (start.Y + movedY) * scale));
                }

                await Delay(i < steps ? stepDelay : releaseDelay, ct);
            }
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        GetCursorPosition(out var cursorAfter);
        var inputScale = Math.Max(TaskContext.Instance().SystemInfo.ScaleTo1080PRatio, 1e-6d);
        var actualX = (cursorAfter.X - cursorBefore.X) / inputScale;
        var actualY = (cursorAfter.Y - cursorBefore.Y) / inputScale;
        LogDetailed(
            "实验传送拖动完成：input=({InputX:0.0},{InputY:0.0}) cursor=({CursorX:0.0},{CursorY:0.0}) multiplier={Multiplier:0.000}",
            end.X - start.X,
            end.Y - start.Y,
            actualX,
            actualY,
            _relativeMoveMultiplier);
        return new DragResult(end.X - start.X, end.Y - start.Y, actualX, actualY);
    }

    public void UpdateRelativeMoveMultiplier(
        double inputX,
        double inputY,
        double actualMapScreenX,
        double actualMapScreenY)
    {
        if (!config.MapDragUseRelativeMove)
        {
            return;
        }

        var initialStrengthMultiplier = double.IsFinite(config.ExperimentalTeleportInitialDragStrength)
            ? Math.Clamp(
                config.ExperimentalTeleportInitialDragStrength,
                TpConfig.MinExperimentalTeleportInitialDragStrength,
                TpConfig.MaxExperimentalTeleportInitialDragStrength)
            : TpConfig.DefaultExperimentalTeleportInitialDragStrength;
        EnsureRelativeMoveMultiplier(initialStrengthMultiplier * InitialDragStrengthScale);

        var inputLength = Math.Sqrt(inputX * inputX + inputY * inputY);
        var actualLength = Math.Sqrt(
            actualMapScreenX * actualMapScreenX + actualMapScreenY * actualMapScreenY);
        var directionDot = inputX * actualMapScreenX + inputY * actualMapScreenY;
        if (inputLength < 20d || actualLength < 5d || directionDot <= 0d)
        {
            LogDetailed(
                "实验传送跳过拖动倍率样本：inputLength={InputLength:0.0} actualLength={ActualLength:0.0} directionDot={DirectionDot:0.0}",
                inputLength,
                actualLength,
                directionDot);
            return;
        }

        var observedMultiplier = inputLength / actualLength;
        if (!double.IsFinite(observedMultiplier) || observedMultiplier is < 0.02d or > 1d)
        {
            LogDetailed(
                "实验传送跳过异常拖动倍率样本：observed={ObservedMultiplier:0.000}",
                observedMultiplier);
            return;
        }

        var previousMultiplier = _relativeMoveMultiplier;
        _relativeMoveMultiplier = previousMultiplier * (1d - EmaWeight) + observedMultiplier * EmaWeight;
        LogDetailed(
            "实验传送更新拖动倍率：previous={PreviousMultiplier:0.000} observed={ObservedMultiplier:0.000} next={NextMultiplier:0.000}",
            previousMultiplier,
            observedMultiplier,
            _relativeMoveMultiplier);
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
        await Delay(GetOperationDelay(50), ct);
        try
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(GetOperationDelay(50), ct);
            DesktopRegion.DesktopRegionMove(
                realRect.X + buttonX * realScale,
                realRect.Y + targetY * realScale);
            await Delay(GetOperationDelay(50), ct);
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        await Delay(GetOperationDelay(50), ct);
    }

    public async Task WaitForStableMapAsync()
    {
        var timeout = GetOperationDelay(StableTimeoutMilliseconds);
        const double threshold = StableDifferenceThreshold;
        var deadline = Environment.TickCount64 + timeout;
        Mat? previous = null;
        var stableFrames = 0;
        var lastDifference = double.NaN;
        var startedAt = Environment.TickCount64;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                ct.ThrowIfCancellationRequested();
                {
                    using var capture = CaptureToRectArea();
                    var roi = BuildStableRegion(capture.CacheGreyMat.Width, capture.CacheGreyMat.Height);
                    using var currentView = new Mat(capture.CacheGreyMat, roi);
                    using var current = currentView.Clone();
                    if (previous != null)
                    {
                        using var difference = new Mat();
                        Cv2.Absdiff(previous, current, difference);
                        lastDifference = Cv2.Mean(difference).Val0;
                        if (lastDifference <= threshold)
                        {
                            stableFrames++;
                            if (stableFrames >= 2)
                            {
                                LogDetailed(
                                    "实验传送地图已稳定：elapsed={ElapsedMilliseconds}ms difference={Difference:0.000} threshold={Threshold:0.000}",
                                    Environment.TickCount64 - startedAt,
                                    lastDifference,
                                    threshold);
                                return;
                            }
                        }
                        else
                        {
                            stableFrames = 0;
                        }
                    }

                    previous?.Dispose();
                    previous = current.Clone();
                }

                await Delay(GetOperationDelay(35), ct);
            }
        }
        finally
        {
            previous?.Dispose();
        }

        LogDetailed(
            "实验传送地图稳定等待达到上限：timeout={TimeoutMilliseconds}ms lastDifference={Difference:0.000} threshold={Threshold:0.000}",
            timeout,
            lastDifference,
            threshold);
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

    private static Rect BuildStableRegion(int width, int height)
    {
        var x = (int)Math.Round(width * 0.30d);
        var y = (int)Math.Round(height * 0.24d);
        var regionWidth = Math.Max(1, (int)Math.Round(width * 0.40d));
        var regionHeight = Math.Max(1, (int)Math.Round(height * 0.52d));
        return new Rect(x, y, Math.Min(regionWidth, width - x), Math.Min(regionHeight, height - y));
    }

    private int GetOperationDelay(int baseDelay)
    {
        var configured = Math.Clamp(
            config.TeleportOperationDelayMilliseconds,
            TpConfig.MinTeleportOperationDelayMilliseconds,
            TpConfig.MaxTeleportOperationDelayMilliseconds);
        return Math.Max(1, (int)Math.Round(
            baseDelay * configured / (double)TpConfig.DefaultTeleportOperationDelayMilliseconds));
    }

    private void LogDetailed(string message, params object?[] args)
    {
        if (config.ExperimentalTeleportDetailedLogs)
        {
            Logger.LogDebug(message, args);
        }
    }

    private static double EaseOut(double value)
    {
        return 1d - Math.Pow(1d - value, 3d);
    }

    private static void GetCursorPosition(out POINT point)
    {
        User32.GetCursorPos(out point);
    }

    private void EnsureRelativeMoveMultiplier(double initialDragStrength)
    {
        if (!config.MapDragUseRelativeMove)
        {
            return;
        }

        if (!double.IsFinite(_relativeMoveInitialStrength) ||
            Math.Abs(_relativeMoveInitialStrength - initialDragStrength) > 1e-6d)
        {
            _relativeMoveInitialStrength = initialDragStrength;
            _relativeMoveMultiplier = initialDragStrength;
        }
    }
}
