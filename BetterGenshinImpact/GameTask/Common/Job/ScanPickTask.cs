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
        await ResetCamera(ct);
        
        for (int n = 0; n < 5; n++) // 最多跑5次
        {
            var hasDrops = false;

            // 旋转视角
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
                    // 把鼠标位置和物品位置重合
                    MoveCursorTo(pickItems.First(), ra);
                    await Delay(100, ct);
                    // 物体越小，距离越远
                    await WalkForward(ct);
                    break;
                }

                Simulation.SendInput.Mouse.MoveMouseBy((int)(300 * _dpi), 0);
                await Delay(100, ct);
            }
            
            if (!hasDrops)
            {
                break;
            }
        }

    }

    private static async Task WalkForward(CancellationToken ct)
    {
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        await Delay(1000, ct);
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
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
        // Simulation.SendInput.Keyboard.Mouse.MiddleButtonClick();
        // await Delay(500, ct);
        Simulation.SendInput.Keyboard.Mouse.MoveMouseBy(0, (int)(500 * _dpi));
        await Delay(100, ct);
    }
}