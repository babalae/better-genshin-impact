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
    private const double StableDifferenceThreshold = 1.5d;
    private const double ZoomButtonX = 47d;
    private const double ZoomStartY = 468d;
    private const double ZoomEndY = 612d;
    private double _relativeMoveMultiplier = double.NaN;
    private double _relativeMoveInitialStrength = double.NaN;
    private double _adaptiveStepIntervalMultiplier = 1d;

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
        var initialDragStrengthScale = double.IsFinite(config.ExperimentalTeleportInitialDragStrengthScale)
            ? Math.Clamp(
                config.ExperimentalTeleportInitialDragStrengthScale,
                TpConfig.MinExperimentalTeleportInitialDragStrengthScale,
                TpConfig.MaxExperimentalTeleportInitialDragStrengthScale)
            : TpConfig.DefaultExperimentalTeleportInitialDragStrengthScale;
        var initialDragStrength = initialDragStrengthMultiplier * initialDragStrengthScale;
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
        await Delay(GetOperationInterval(), ct);
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
        var maxDragSteps = Math.Clamp(
            config.ExperimentalTeleportMaxDragSteps,
            TpConfig.MinExperimentalTeleportMaxDragSteps,
            TpConfig.MaxExperimentalTeleportMaxDragSteps);
        var stepProfileFactor = double.IsFinite(config.ExperimentalTeleportStepProfileFactor)
            ? Math.Clamp(
                config.ExperimentalTeleportStepProfileFactor,
                TpConfig.MinExperimentalTeleportStepProfileFactor,
                TpConfig.MaxExperimentalTeleportStepProfileFactor)
            : TpConfig.DefaultExperimentalTeleportStepProfileFactor;
        var steps = Math.Clamp(
            (int)Math.Ceiling(inputDistance * stepProfileFactor / maxSingleStepDistance),
            5,
            maxDragSteps);
        var movedX = 0d;
        var movedY = 0d;
        var stepDelay = GetAdaptiveStepInterval();
        var releaseDelay = GetOperationInterval();
        LogDetailed(
            "实验传送拖动参数：theory=({TheoryX:0.0},{TheoryY:0.0}) theoryDistance={TheoryDistance:0.0} " +
            "emaMultiplier={EmaMultiplier:0.000} emaSource={EmaSource} desiredInput=({DesiredX:0.0},{DesiredY:0.0}) " +
            "desiredDistance={DesiredDistance:0.0} runwayDistance={RunwayDistance:0.0} runwayRatio={RunwayRatio:0.000} " +
            "runwayDelta=({RunwayDeltaX:0.0},{RunwayDeltaY:0.0}) " +
            "maxStepDistance={MaxStepDistance:0.0} profileFactor={ProfileFactor:0.000} steps={Steps} " +
            "configuredStepDelay={ConfiguredStepDelay}ms adaptiveStepMultiplier={AdaptiveStepMultiplier:0.000} " +
            "effectiveStepDelay={EffectiveStepDelay}ms operationDelay={OperationDelay}ms releaseDelay={ReleaseDelay}ms",
            requestedDeltaX,
            requestedDeltaY,
            requestedDistance,
            moveRatio,
            ratioSource,
            desiredX,
            desiredY,
            desiredDistance,
            inputDistance,
            runwayRatio,
            end.X - start.X,
            end.Y - start.Y,
            maxSingleStepDistance,
            stepProfileFactor,
            steps,
            Math.Clamp(
                config.ExperimentalTeleportDragStepIntervalMilliseconds,
                TpConfig.MinExperimentalTeleportDragStepIntervalMilliseconds,
                TpConfig.MaxExperimentalTeleportDragStepIntervalMilliseconds),
            _adaptiveStepIntervalMultiplier,
            stepDelay,
            GetOperationInterval(),
            releaseDelay);
        var dragStartedAt = Environment.TickCount64;
        try
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(GetOperationInterval(), ct);
            for (var i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                var progress = SmoothStep(i / (double)steps);
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

        await Delay(GetOperationInterval(), ct);
        GetCursorPosition(out var cursorAfter);
        var inputScale = Math.Max(TaskContext.Instance().SystemInfo.ScaleTo1080PRatio, 1e-6d);
        var actualX = (cursorAfter.X - cursorBefore.X) / inputScale;
        var actualY = (cursorAfter.Y - cursorBefore.Y) / inputScale;
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
            _relativeMoveMultiplier,
            Environment.TickCount64 - dragStartedAt);
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
        var initialDragStrengthScale = double.IsFinite(config.ExperimentalTeleportInitialDragStrengthScale)
            ? Math.Clamp(
                config.ExperimentalTeleportInitialDragStrengthScale,
                TpConfig.MinExperimentalTeleportInitialDragStrengthScale,
                TpConfig.MaxExperimentalTeleportInitialDragStrengthScale)
            : TpConfig.DefaultExperimentalTeleportInitialDragStrengthScale;
        EnsureRelativeMoveMultiplier(initialStrengthMultiplier * initialDragStrengthScale);

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

        var emaWeight = double.IsFinite(config.ExperimentalTeleportEmaWeight)
            ? Math.Clamp(
                config.ExperimentalTeleportEmaWeight,
                TpConfig.MinExperimentalTeleportEmaWeight,
                TpConfig.MaxExperimentalTeleportEmaWeight)
            : TpConfig.DefaultExperimentalTeleportEmaWeight;
        var previousMultiplier = _relativeMoveMultiplier;
        _relativeMoveMultiplier = previousMultiplier * (1d - emaWeight) + observedMultiplier * emaWeight;
        LogDetailed(
            "实验传送更新拖动倍率：previous={PreviousMultiplier:0.000} observed={ObservedMultiplier:0.000} next={NextMultiplier:0.000}",
            previousMultiplier,
            observedMultiplier,
            _relativeMoveMultiplier);
    }

    public void ReportMapMovementOutcome(bool moved)
    {
        var slowdownFactor = double.IsFinite(config.ExperimentalTeleportInputLossSlowdownFactor)
            ? Math.Clamp(
                config.ExperimentalTeleportInputLossSlowdownFactor,
                TpConfig.MinExperimentalTeleportInputLossSlowdownFactor,
                TpConfig.MaxExperimentalTeleportInputLossSlowdownFactor)
            : TpConfig.DefaultExperimentalTeleportInputLossSlowdownFactor;
        var recoveryFactor = double.IsFinite(config.ExperimentalTeleportInputRecoveryFactor)
            ? Math.Clamp(
                config.ExperimentalTeleportInputRecoveryFactor,
                TpConfig.MinExperimentalTeleportInputRecoveryFactor,
                TpConfig.MaxExperimentalTeleportInputRecoveryFactor)
            : TpConfig.DefaultExperimentalTeleportInputRecoveryFactor;

        var configuredStepInterval = Math.Clamp(
            config.ExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MinExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MaxExperimentalTeleportDragStepIntervalMilliseconds);
        var previousMultiplier = _adaptiveStepIntervalMultiplier;
        if (moved)
        {
            _adaptiveStepIntervalMultiplier = Math.Max(
                1d,
                _adaptiveStepIntervalMultiplier * recoveryFactor);
            LogDetailed(
                "实验传送地图移动有效，自适应步进恢复：moved={Moved} previousMultiplier={PreviousMultiplier:0.000} " +
                "recoveryFactor={RecoveryFactor:0.000} multiplier={Multiplier:0.000} configuredStepDelay={ConfiguredStepDelay}ms " +
                "effectiveStepDelay={EffectiveStepDelay}ms",
                moved,
                previousMultiplier,
                recoveryFactor,
                _adaptiveStepIntervalMultiplier,
                configuredStepInterval,
                GetAdaptiveStepInterval());
            return;
        }

        var maximumMultiplier = TpConfig.MaxExperimentalTeleportDragStepIntervalMilliseconds /
                                (double)configuredStepInterval;
        _adaptiveStepIntervalMultiplier = Math.Min(
            maximumMultiplier,
            _adaptiveStepIntervalMultiplier * slowdownFactor);
        LogDetailed(
            "实验传送检测到地图未移动，增加拖动步进间隔：moved={Moved} previousMultiplier={PreviousMultiplier:0.000} " +
            "slowdownFactor={SlowdownFactor:0.000} multiplier={Multiplier:0.000} configuredStepDelay={ConfiguredStepDelay}ms " +
            "effectiveStepDelay={EffectiveStepDelay}ms maximumMultiplier={MaximumMultiplier:0.000}",
            moved,
            previousMultiplier,
            slowdownFactor,
            _adaptiveStepIntervalMultiplier,
            configuredStepInterval,
            GetAdaptiveStepInterval(),
            maximumMultiplier);
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

    public async Task WaitForStableMapAsync()
    {
        var timeout = GetMapStabilityTimeout();
        var detectionInterval = GetMapStabilityInterval();
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
                                    "实验传送地图已稳定：elapsed={ElapsedMilliseconds}ms difference={Difference:0.000} " +
                                    "threshold={Threshold:0.000} stableFrames={StableFrames} detectionInterval={DetectionInterval}ms " +
                                    "timeout={Timeout}ms",
                                    Environment.TickCount64 - startedAt,
                                    lastDifference,
                                    threshold,
                                    stableFrames,
                                    detectionInterval,
                                    timeout);
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

                await Delay(detectionInterval, ct);
            }
        }
        finally
        {
            previous?.Dispose();
        }

        LogDetailed(
            "实验传送地图稳定等待达到上限：elapsed={ElapsedMilliseconds}ms timeout={TimeoutMilliseconds}ms " +
            "lastDifference={Difference:0.000} threshold={Threshold:0.000} stableFrames={StableFrames} " +
            "detectionInterval={DetectionInterval}ms",
            Environment.TickCount64 - startedAt,
            timeout,
            lastDifference,
            threshold,
            stableFrames,
            detectionInterval);
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

    private int GetOperationInterval()
    {
        return Math.Clamp(
            config.ExperimentalTeleportOperationIntervalMilliseconds,
            TpConfig.MinExperimentalTeleportOperationIntervalMilliseconds,
            TpConfig.MaxExperimentalTeleportOperationIntervalMilliseconds);
    }

    private int GetMapStabilityInterval()
    {
        return Math.Clamp(
            config.ExperimentalTeleportMapStabilityIntervalMilliseconds,
            TpConfig.MinExperimentalTeleportMapStabilityIntervalMilliseconds,
            TpConfig.MaxExperimentalTeleportMapStabilityIntervalMilliseconds);
    }

    private int GetMapStabilityTimeout()
    {
        return Math.Clamp(
            config.ExperimentalTeleportMapStabilityTimeoutMilliseconds,
            TpConfig.MinExperimentalTeleportMapStabilityTimeoutMilliseconds,
            TpConfig.MaxExperimentalTeleportMapStabilityTimeoutMilliseconds);
    }

    private int GetAdaptiveStepInterval()
    {
        var configured = Math.Clamp(
            config.ExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MinExperimentalTeleportDragStepIntervalMilliseconds,
            TpConfig.MaxExperimentalTeleportDragStepIntervalMilliseconds);
        var adaptive = (int)Math.Round(configured * _adaptiveStepIntervalMultiplier);
        return Math.Clamp(adaptive, configured, TpConfig.MaxExperimentalTeleportDragStepIntervalMilliseconds);
    }

    private void LogDetailed(string message, params object?[] args)
    {
        if (config.ExperimentalTeleportDetailedLogs)
        {
            Logger.LogDebug(message, args);
        }
    }

    private static double SmoothStep(double value)
    {
        return value * value * (3d - 2d * value);
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
