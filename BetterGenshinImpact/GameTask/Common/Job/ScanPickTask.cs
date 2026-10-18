using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
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
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 构造时的输入坐标缩放比例快照
    /// </summary>
    private readonly double _dpi;

    /// <summary>
    /// 构造时的捕获窗口区域快照
    /// </summary>
    private readonly RECT _realCaptureRect;

    /// <summary>
    /// 创建扫描拾取任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public ScanPickTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _dpi = runtime.SystemInfo.DpiScale;
        _realCaptureRect = runtime.SystemInfo.CaptureAreaRect;
    }

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
            _runtime.MaskWindowDrawingBoard.ClearAll();
        }
    }

    public async Task DoOnce(CancellationToken ct, int? seconds = null)
    {
        // 每次扫描独立持有模型，结束或取消时释放，允许同一任务实例再次扫描。
        using var predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);
        var sec = seconds ?? _config.AutoFightConfig.PickDropsAfterFightSeconds;
        Stopwatch timeoutStopwatch = Stopwatch.StartNew();
        TimeSpan finishTime = TimeSpan.FromSeconds(sec);

        InputHub.Foreground.SimulateAction(GIActions.Drop);
        await ResetCamera(ct);

        while (!ct.IsCancellationRequested && timeoutStopwatch.Elapsed < finishTime)
        {
            var (hasItems, pickItems) = DetectPickableItems(predictor);
            // Logger.LogInformation("存在可拾取物品: {0}", hasItems);
            if (!hasItems)
            {
                InputHub.ReleaseAll();
                await ResetCamera(ct);
                for (var i = 0; i < 10 && timeoutStopwatch.Elapsed < finishTime; i++)
                {
                    InputHub.Foreground.Mouse.MoveMouseBy(400, 0);
                    if (i > 5) //前期不考虑移动扫描
                        await WalkByDirection(ct, GIActions.MoveForward, 100);
                    InputHub.Foreground.SimulateAction(GIActions.Drop);
                    await _runtime.Delay(300, ct);
                    (hasItems, pickItems) = DetectPickableItems(predictor);
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

            // Assume 1080p resolution
            // approximate dist=(x-960)**2+14*(y-888.88)**2

            pickItems = pickItems.OrderBy(rect => Math.Pow(rect.X - 960, 2) +
                14 * Math.Pow(rect.Bottom - 888.88, 2)).ToList();
            var toPickItem = pickItems[0];
            Logger.LogDebug("Fetching: {0}", toPickItem);
            Logger.LogDebug("Using coord: {0} {1}", toPickItem.X, toPickItem.Bottom);
            MoveTowardsItem(toPickItem);

            await _runtime.Delay(200, ct);
            InputHub.Foreground.SimulateAction(GIActions.Drop);
        }
        Logger.LogInformation("超时或视野内没有可拾取物品，结束扫描");
        InputHub.ReleaseAll();
        InputHub.Foreground.SimulateAction(GIActions.Drop);
    }

    /// <summary>
    /// Moves the character towards the specified item by controlling movement keys
    /// </summary>
    /// <param name="toPickItem">The item to move towards</param>
    private static void MoveTowardsItem(Rect toPickItem)
    {
        // 需要避免两个对向的键同时按下
        // 左右转向不再受物品底边 y 坐标限制：无论远近都按物品中心相对屏幕中心（960）的横向偏移转向，
        // 避免远处斜向掉落物只能直行靠近、无法拾取的问题
        var itemCenterX = toPickItem.X + toPickItem.Width / 2.0;
        if (itemCenterX < 880)
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
        }
        else if (itemCenterX > 1040)
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveRight, KeyType.KeyDown);
        }
        else
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
        }

        if (toPickItem.Bottom < 770)
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
        else if (toPickItem.Bottom > 900)
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
        }
        else
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// Detects pickable items in the current view
    /// </summary>
    /// <returns>A tuple containing whether items were found and the list of pickable items</returns>
    private (bool hasItems, List<Rect> pickItems) DetectPickableItems(BgiYoloPredictor predictor)
    {
        using var ra = _runtime.CaptureToRectArea();
        var resultDic = predictor.Detect(ra);
        // 过滤出可拾取物品
        var pickItems = resultDic.Where(x => x.Key is "drops" or "ore")
            .SelectMany(x => x.Value).ToList();
        return (pickItems.Count > 0, pickItems);
    }

    private async Task WalkByDirection(CancellationToken ct, GIActions act, int ms = 1000)
    {
        InputHub.Foreground.SimulateAction(act, KeyType.KeyDown);
        await _runtime.Delay(ms, ct);
        InputHub.Foreground.SimulateAction(act, KeyType.KeyUp);
    }

    // 回正 并下移视角
    private async Task ResetCamera(CancellationToken ct)
    {
        InputHub.Foreground.Mouse.MiddleButtonClick();
        await _runtime.Delay(500, ct);
        InputHub.Foreground.Mouse.MoveMouseBy(0, (int)(500 * _dpi));
        await _runtime.Delay(100, ct);
    }
}
