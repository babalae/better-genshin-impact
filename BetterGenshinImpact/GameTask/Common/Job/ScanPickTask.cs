using System;
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
        var forwardTimes = TaskContext.Instance().Config.AutoFightConfig.PickDropsConfig.ForwardTimes;
        for (int n = 0; n < forwardTimes; n++) // 直走次数
        {
            await ResetCamera(ct);
            var hasDrops = false;

            // 旋转视角
            var step = 300 * _dpi; // TODO:把300换成一个更加普适的值
            step = n % 2 == 0 ? step : -step;
            for (var i = 0; i < 20; i++)
            {
                var ra = CaptureToRectArea();
                var resultDic = _predictor.Detect(ra);
                // 过滤出可拾取物品
                var pickItems = resultDic.Where(x => x.Key is "drops" or "ore")
                    .SelectMany(x => x.Value).ToList();


                if (pickItems.Count > 0)
                {
                    hasDrops = true;
                    await MoveTowardsFirstDrop(ct, 300 * _dpi);
                    break;
                }

                Simulation.SendInput.Mouse.MoveMouseBy((int)step, 0);
                await Delay(100, ct);
            }

            if (!hasDrops)
            {
                break;
            }
        }

    }

    private static async Task WalkForward(CancellationToken ct, int ms = 1000)
    {
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        await Delay(ms, ct);
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
    }

    private async Task MoveTowardsFirstDrop(CancellationToken ct, double step)
    {
        //通过每次缩小之前的步长来定位，可能有一定开销
        var decayFactor = TaskContext.Instance().Config.AutoFightConfig.PickDropsConfig.DecayFactor;
        var calibrationTimes = TaskContext.Instance().Config.AutoFightConfig.PickDropsConfig.CalibrationTimes;

        var found = false;
        for (var i = 0; i < calibrationTimes; i++)
        {
            var ra = CaptureToRectArea();
            var resultDic = _predictor.Detect(ra);
            var pickItems = resultDic.Where(x => x.Key is "drops" or "ore")
                .SelectMany(x => x.Value).ToList();
            if (pickItems.Count > 0)
            {
                step *= decayFactor;
                found = true;
                //只关心横坐标
                var centerX = (pickItems.First().Left + pickItems.First().Right) / 2;
                var dx = centerX - ra.Width / 2;
                if (dx > 0)
                    Simulation.SendInput.Mouse.MoveMouseBy((int)step, 0);
                else if (dx < 0)
                    Simulation.SendInput.Mouse.MoveMouseBy(-(int)step, 0);
                await Delay(100, ct);
            }
            else
            {
                //也许已经对准，被人物挡住
                break;
            }
        }
        if (found) //仅在找到物品时前进（在误判进入该函数时避免远离原地）
        {
            Logger.LogInformation("前进采集");
            var forwardms = TaskContext.Instance().Config.AutoFightConfig.PickDropsConfig.ForwardSeconds * 1000;
            if (forwardms == 0)
                forwardms = new Random().Next(1000, 3000);

            await WalkForward(ct, forwardms);
        }
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