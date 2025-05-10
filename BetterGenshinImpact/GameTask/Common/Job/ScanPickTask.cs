using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
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
    private readonly BgiYoloPredictor _predictor = BgiOnnxFactory.Instance.CreateYoloPredictor(BgiOnnxModel.BgiWorld);
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
            var (hasItems, pickItems) = DetectPickableItems();
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
                    (hasItems, pickItems) = DetectPickableItems();
                    if (hasItems) break;
                }
            }

            if (!hasItems) break;

            // Assume 1080p resolution
            // approximate dist=(x-960)**2+14*(y-888.88)**2

            pickItems = pickItems.OrderBy(rect => Math.Pow(rect.X - 960, 2) +
                14 * Math.Pow(rect.Bottom - 888.88, 2)).ToList();
            var toPickItem = pickItems[0];
            Logger.LogDebug("Fetching: {0}", toPickItem);
            Logger.LogDebug("Using coord: {0} {1}", toPickItem.X, toPickItem.Bottom);
            MoveTowardsItem(toPickItem);

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
    private static void MoveTowardsItem(Rect toPickItem)
    {
        // 对于比较远的物品（Y坐标靠上）先用前进靠近
        // 需要避免两个对向的键同时按下
        if (toPickItem.Bottom > 560)
        {
            if (toPickItem.X < 760)
            {
                Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
                Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
            }
            else if (toPickItem.X > 1040)
            {
                Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyDown);
            }
            else
            {
                Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
            }
        }

        if (toPickItem.Bottom < 770)
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
        else if (toPickItem.Bottom > 900)
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
    private (bool hasItems, List<Rect> pickItems) DetectPickableItems()
    {
        var ra = CaptureToRectArea();
        var resultDic = _predictor.Detect(ra);
        // 过滤出可拾取物品
        var pickItems = resultDic.Where(x => x.Key is "drops" or "ore")
            .SelectMany(x => x.Value).ToList();
        return (pickItems.Count > 0, pickItems);
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