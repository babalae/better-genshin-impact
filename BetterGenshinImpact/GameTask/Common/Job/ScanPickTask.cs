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
    private readonly BgiYoloV8Predictor _predictor = BgiYoloV8PredictorFactory.GetPredictor(@"Assets\Model\World\bgi_world.onnx");
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

        await ResetCamera(ct);
        Simulation.SendInput.SimulateAction(GIActions.Drop);
        while (!ct.IsCancellationRequested && timeoutStopwatch.Elapsed < finishTime)
        {

            Simulation.SendInput.SimulateAction(GIActions.Drop);
            var (hasItems, pickItems) = DetectPickableItems(ct);
            // Logger.LogInformation("存在可拾取物品: {0}", hasItems);
            if (!hasItems)
            {
                Simulation.ReleaseAllKey();
                await ResetCamera(ct);
                for (var i = 0; i < 20; i++)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(600, 0);
                    await WalkBack(ct, 100);
                    Simulation.SendInput.SimulateAction(GIActions.Drop);
                    (hasItems, pickItems) = DetectPickableItems(ct);
                    if (hasItems) break;
                }
            }

            if (!hasItems)
            {
                Logger.LogInformation("没有可拾取物品，结束扫描");
                break;
            }


            // Assume 1080p resolution
            // approximate dist=(x-960)**2+14*(y-888.88)**2

            pickItems = pickItems.OrderBy(rect => Math.Pow(rect.X - 960, 2) +
                14 * Math.Pow(rect.Bottom - 888.88, 2)).ToList();
            var toPickItem = pickItems[0];
            Logger.LogDebug("Fetching: {0}", toPickItem);
            Logger.LogDebug("Using coord: {0} {1}", toPickItem.X, toPickItem.Bottom);

            // 需要避免两个对向的键同时按下
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
            await Delay(100, ct);
        }
        Simulation.ReleaseAllKey();
        Simulation.SendInput.SimulateAction(GIActions.Drop);
    }

    /// <summary>
    /// Detects pickable items in the current view
    /// </summary>
    /// <returns>A tuple containing whether items were found and the list of pickable items</returns>
    private (bool hasItems, List<Rect> pickItems) DetectPickableItems(CancellationToken ct)
    {
        Delay(100, ct).Wait(ct);
        var ra = CaptureToRectArea();
        var resultDic = _predictor.Detect(ra);
        // 过滤出可拾取物品
        var pickItems = resultDic.Where(x => x.Key is "drops" or "ore")
            .SelectMany(x => x.Value).ToList();
        return (pickItems.Count > 0, pickItems);
    }

    private static async Task WalkBack(CancellationToken ct, int ms = 1000)
    {
        Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
        await Delay(ms, ct);
        Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
    }

    private void MoveCursorTo(Rect item, ImageRegion ra)
    {
        var centerX = (item.Left + item.Right) / 2;
        var centerY = (item.Top + item.Bottom) / 2;
        var dx = centerX - ra.Width / 2;
        var dy = centerY - ra.Height / 2;
        var r = _realCaptureRect.Width * 1.0 / ra.Width; // 缩放比例
        Simulation.SendInput.Mouse.MoveMouseBy((int)(dx * r * _dpi), (int)(dy * r * _dpi));
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