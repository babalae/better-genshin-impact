using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 扫描拾取任务
/// 请在安全地区使用
/// </summary>
public class ScanPickTask
{
    private const string LootPillarFallbackOverlayKey = "ScanPickLootPillarFallback";

    private readonly BgiYoloPredictor _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);
    private readonly double _dpi = TaskContext.Instance().DpiScale;
    private readonly RECT _realCaptureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;


    public async Task Start(CancellationToken ct)
    {
        try
        {
            await DoOnce(ct);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "拾取周边物品异常");
            Logger.LogError("拾取周边物品异常: {Msg}", e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    public async Task DoOnce(CancellationToken ct)
    {
        var sec = TaskContext.Instance().Config.AutoFightConfig.PickDropsAfterFightSeconds;
        Stopwatch timeoutStopwatch = Stopwatch.StartNew();
        TimeSpan finishTime = TimeSpan.FromSeconds(sec);

        Simulation.SendInput.SimulateAction(GIActions.Drop);
        await ResetCamera(ct);

        while (!ct.IsCancellationRequested && timeoutStopwatch.Elapsed < finishTime)
        {
            var (hasItems, pickItems, frameSize) = DetectPickableItems();
            // Logger.LogInformation("存在可拾取物品: {0}", hasItems);
            if (!hasItems)
            {
                Simulation.ReleaseAllKey();
                await ResetCamera(ct);
                for (var i = 0; i < 10; i++)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(400, 0);
                    if (i > 5) //前期不考虑移动扫描
                        await WalkByDirection(ct, GIActions.MoveForward, 100);
                    Simulation.SendInput.SimulateAction(GIActions.Drop);
                    await Delay(300, ct);
                    (hasItems, pickItems, frameSize) = DetectPickableItems();
                    if (hasItems) break;
                }
            }

            if (!hasItems) break;

            pickItems = SortPickItems(pickItems, frameSize.Width, frameSize.Height).ToList();
            var toPickItem = pickItems[0];
            Logger.LogDebug("Fetching: {0}", toPickItem);
            Logger.LogDebug("Using coord: {0} {1}", toPickItem.X, toPickItem.Bottom);
            MoveTowardsItem(toPickItem, frameSize.Width, frameSize.Height);

            await Delay(200, ct);
            Simulation.SendInput.SimulateAction(GIActions.Drop);
        }
        Logger.LogInformation("超时或视野内没有可拾取物品，结束扫描");
        Simulation.ReleaseAllKey();
        Simulation.SendInput.SimulateAction(GIActions.Drop);
    }

    /// <summary>
    /// Moves the character towards the specified item by controlling movement keys
    /// </summary>
    /// <param name="toPickItem">The item to move towards</param>
    private static void MoveTowardsItem(Rect toPickItem, int frameWidth, int frameHeight)
    {
        var decision = GetMovementDecision(toPickItem, frameWidth, frameHeight);

        // 对于比较远的物品（Y坐标靠上）先用前进靠近
        // 需要避免两个对向的键同时按下
        if (decision.Left)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
        }
        else if (decision.Right)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyDown);
        }
        else
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
        }

        if (decision.Forward)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
        else if (decision.Backward)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
        }
        else
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// Detects pickable items in the current view
    /// </summary>
    /// <returns>A tuple containing whether items were found and the list of pickable items</returns>
    private (bool hasItems, List<Rect> pickItems, Size frameSize) DetectPickableItems()
    {
        using var ra = CaptureToRectArea();
        var resultDic = _predictor.Detect(ra);
        // 过滤出可拾取物品
        var pickItems = resultDic.Where(x => x.Key is "drops" or "ore")
            .SelectMany(x => x.Value).ToList();
        var frameSize = new Size(ra.Width, ra.Height);
        var yoloPickCount = pickItems.Count;

        if (pickItems.Count == 0)
        {
            pickItems = DetectGreenLootPillars(ra.SrcMat);
            if (pickItems.Count > 0)
            {
                Logger.LogInformation("YOLO未识别到掉落物，光柱兜底识别到 {Count} 个候选", pickItems.Count);
            }
        }

        DrawFallbackPillars(ra, yoloPickCount == 0 ? pickItems : []);
        Logger.LogDebug("拾取扫描识别候选: YOLO={YoloCount}, 光柱兜底={FallbackCount}, 尺寸={Width}x{Height}",
            yoloPickCount, yoloPickCount == 0 ? pickItems.Count : 0, frameSize.Width, frameSize.Height);

        return (pickItems.Count > 0, pickItems, frameSize);
    }

    internal static IEnumerable<Rect> SortPickItems(IEnumerable<Rect> pickItems, int frameWidth, int frameHeight)
    {
        var targetX = frameWidth * 0.5d;
        var targetBottom = frameHeight * (888.88d / 1080d);

        return pickItems.OrderBy(rect =>
        {
            var centerX = rect.X + rect.Width / 2d;
            return Math.Pow(centerX - targetX, 2) + 14 * Math.Pow(rect.Bottom - targetBottom, 2);
        });
    }

    internal readonly record struct MovementDecision(bool Left, bool Right, bool Forward, bool Backward);

    internal static MovementDecision GetMovementDecision(Rect toPickItem, int frameWidth, int frameHeight)
    {
        var itemCenterX = toPickItem.X + toPickItem.Width / 2d;
        var leftThreshold = frameWidth * (760d / 1920d);
        var rightThreshold = frameWidth * (1040d / 1920d);
        var horizontalStartBottom = frameHeight * (560d / 1080d);
        var forwardThreshold = frameHeight * (770d / 1080d);
        var backwardThreshold = frameHeight * (900d / 1080d);

        var left = false;
        var right = false;
        if (toPickItem.Bottom > horizontalStartBottom)
        {
            left = itemCenterX < leftThreshold;
            right = itemCenterX > rightThreshold;
        }

        var forward = toPickItem.Bottom < forwardThreshold;
        var backward = toPickItem.Bottom > backwardThreshold;
        return new MovementDecision(left, right, forward, backward);
    }

    internal static List<Rect> DetectGreenLootPillars(Mat srcMat)
    {
        if (srcMat.Empty())
        {
            return [];
        }

        using var bgr = new Mat();
        switch (srcMat.Channels())
        {
            case 4:
                Cv2.CvtColor(srcMat, bgr, ColorConversionCodes.BGRA2BGR);
                break;
            case 3:
                srcMat.CopyTo(bgr);
                break;
            case 1:
                Cv2.CvtColor(srcMat, bgr, ColorConversionCodes.GRAY2BGR);
                break;
            default:
                return [];
        }

        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(35, 60, 140), new Scalar(95, 255, 255), mask);
        ClearLootFallbackIgnoredAreas(mask);

        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 9));
        using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel);
        Cv2.Dilate(mask, mask, dilateKernel);

        Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var candidates = new List<Rect>();
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (IsLootPillarCandidate(rect, srcMat.Width, srcMat.Height, mask, Cv2.ContourArea(contour)))
            {
                candidates.Add(rect);
            }
        }

        return MergeLootPillarCandidates(candidates, srcMat.Width, srcMat.Height);
    }

    private static void DrawFallbackPillars(ImageRegion ra, List<Rect> fallbackPickItems)
    {
        if (fallbackPickItems.Count == 0)
        {
            VisionContext.Instance().DrawContent.PutOrRemoveRectList(LootPillarFallbackOverlayKey, null);
            return;
        }

        var drawList = fallbackPickItems
            .Select(rect => ra.ToRectDrawable(rect, LootPillarFallbackOverlayKey, System.Drawing.Pens.Lime))
            .ToList();
        VisionContext.Instance().DrawContent.PutOrRemoveRectList(LootPillarFallbackOverlayKey, drawList);
    }

    private static bool IsLootPillarCandidate(Rect rect, int frameWidth, int frameHeight, Mat mask, double contourArea)
    {
        if (rect.Width < 4 || rect.Height < Math.Max(38, frameHeight * 0.035d))
        {
            return false;
        }

        if (rect.Width > frameWidth * 0.065d)
        {
            return false;
        }

        if (rect.Height / (double)rect.Width < 1.35d)
        {
            return false;
        }

        if (contourArea < 70d)
        {
            return false;
        }

        var centerX = rect.X + rect.Width / 2d;
        if (centerX < frameWidth * 0.18d || centerX > frameWidth * 0.84d)
        {
            return false;
        }

        if (rect.Y > frameHeight * 0.92d)
        {
            return false;
        }

        var clamped = rect.ClampTo(mask);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return false;
        }

        using var roi = new Mat(mask, clamped);
        return Cv2.CountNonZero(roi) >= 35;
    }

    private static void ClearLootFallbackIgnoredAreas(Mat mask)
    {
        var width = mask.Width;
        var height = mask.Height;
        ClearMaskRect(mask, new Rect(0, 0, width, (int)(height * 0.35d)));
        ClearMaskRect(mask, new Rect((int)(width * 0.84d), 0, width, height));
        ClearMaskRect(mask, new Rect(0, (int)(height * 0.92d), width, height));
        ClearMaskRect(mask, new Rect(0, (int)(height * 0.48d), (int)(width * 0.23d), height));
    }

    private static void ClearMaskRect(Mat mask, Rect rect)
    {
        var clamped = rect.ClampTo(mask);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return;
        }

        using var roi = new Mat(mask, clamped);
        roi.SetTo(Scalar.All(0));
    }

    private static List<Rect> MergeLootPillarCandidates(List<Rect> candidates, int frameWidth, int frameHeight)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var merged = new List<Rect>();
        foreach (var candidate in candidates.OrderBy(rect => rect.X).ThenBy(rect => rect.Y))
        {
            var index = merged.FindIndex(existing => ShouldMergeLootPillar(existing, candidate, frameWidth));
            if (index < 0)
            {
                merged.Add(candidate);
                continue;
            }

            merged[index] = Union(merged[index], candidate).ClampTo(frameWidth, frameHeight);
        }

        return merged;
    }

    private static bool ShouldMergeLootPillar(Rect a, Rect b, int frameWidth)
    {
        var horizontalGap = Math.Max(0, Math.Max(a.X, b.X) - Math.Min(a.Right, b.Right));
        var verticalGap = Math.Max(0, Math.Max(a.Y, b.Y) - Math.Min(a.Bottom, b.Bottom));
        var maxHorizontalGap = Math.Max(16, frameWidth * 0.018d);
        return horizontalGap <= maxHorizontalGap && verticalGap <= 42;
    }

    private static Rect Union(Rect a, Rect b)
    {
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.Right, b.Right);
        var y2 = Math.Max(a.Bottom, b.Bottom);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static async Task WalkByDirection(CancellationToken ct, GIActions act, int ms = 1000)
    {
        Simulation.SendInput.SimulateAction(act, KeyType.KeyDown);
        await Delay(ms, ct);
        Simulation.SendInput.SimulateAction(act, KeyType.KeyUp);
    }

    // 回正 并下移视角
    private async Task ResetCamera(CancellationToken ct)
    {
        Simulation.SendInput.Keyboard.Mouse.MiddleButtonClick();
        await Delay(500, ct);
        Simulation.SendInput.Keyboard.Mouse.MoveMouseBy(0, (int)(500 * _dpi));
        await Delay(100, ct);
    }
}
