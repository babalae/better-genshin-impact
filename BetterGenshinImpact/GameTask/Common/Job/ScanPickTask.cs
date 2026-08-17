using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
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
    private readonly BgiYoloPredictor _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);
    private readonly double _dpi = TaskContext.Instance().DpiScale;
    private readonly RECT _realCaptureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;


    public async Task Start(CancellationToken ct, int? seconds = null)
    {
        try
        {
            await DoOnce(ct, seconds);
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

    public async Task DoOnce(CancellationToken ct, int? seconds = null)
    {
        var sec = seconds ?? TaskContext.Instance().Config.AutoFightConfig.PickDropsAfterFightSeconds;
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
                for (var i = 0; i < 10 && timeoutStopwatch.Elapsed < finishTime; i++)
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

            // 一整圈都没有发现物品时，不要提前结束扫描，继续按配置时长扫描
            if (!hasItems)
            {
                continue;
            }

            // 扫圈中命中物品时相机已转到物品方向，保持当前视角直接移动；
            // 检测坐标只在当前视角下有效，回正相机会让物品移出视野、坐标失效

            pickItems = SortPickItems(pickItems, frameSize.Width, frameSize.Height).ToList();
            var toPickItem = pickItems[0];
            Logger.LogDebug("Fetching: {0}", toPickItem);
            Logger.LogDebug("Using coord: {0} {1}", toPickItem.X, toPickItem.Bottom);
            var movementDecision = GetMovementDecision(toPickItem, frameSize.Width, frameSize.Height);
            if (movementDecision.Pickup)
            {
                Simulation.ReleaseAllKey();
                AutoPickAssets.Get(frameSize.Width, frameSize.Height, TaskContext.Instance().Config.AutoPickConfig.PickKey)
                    .PressPickKey();
            }
            else
            {
                MoveTowardsItem(toPickItem, frameSize.Width, frameSize.Height);
            }

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
        Logger.LogDebug("拾取扫描YOLO候选: {YoloCount}, 尺寸={Width}x{Height}",
            pickItems.Count, frameSize.Width, frameSize.Height);

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

    internal readonly record struct MovementDecision(bool Left, bool Right, bool Forward, bool Backward, bool Pickup);

    internal static MovementDecision GetMovementDecision(Rect toPickItem, int frameWidth, int frameHeight)
    {
        var itemCenterX = toPickItem.X + toPickItem.Width / 2d;
        var leftThreshold = frameWidth * (760d / 1920d);
        var rightThreshold = frameWidth * (1040d / 1920d);
        var forwardThreshold = frameHeight * (770d / 1080d);
        var backwardThreshold = frameHeight * (900d / 1080d);
        var pickupMaxBottom = frameHeight * (1020d / 1080d);

        var pickup = itemCenterX >= leftThreshold
                     && itemCenterX <= rightThreshold
                     && toPickItem.Bottom >= forwardThreshold
                     && toPickItem.Bottom <= pickupMaxBottom;
        if (pickup)
        {
            return new MovementDecision(false, false, false, false, true);
        }

        var left = itemCenterX < leftThreshold;
        var right = itemCenterX > rightThreshold;

        var forward = toPickItem.Bottom < forwardThreshold;
        var backward = toPickItem.Bottom > backwardThreshold;
        return new MovementDecision(left, right, forward, backward, false);
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
