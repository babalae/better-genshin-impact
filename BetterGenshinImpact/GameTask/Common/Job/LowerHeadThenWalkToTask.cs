using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 低头找指定图标
/// </summary>
public class LowerHeadThenWalkToTask
{
    private Rect CaptureRect => TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;

    private double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    private readonly RecognitionObject _trackPoint;

    private int _timeoutMilliseconds;


    public LowerHeadThenWalkToTask(string targetMatName, int timeoutMilliseconds = 30000)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
        _trackPoint = new RecognitionObject
        {
            Name = "TrackPoint",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", targetMatName),
            RegionOfInterest = new Rect((int)(300 * AssetScale), 0, CaptureRect.Width - (int)(600 * AssetScale), CaptureRect.Height),
            Threshold = 0.6,
            DrawOnWindow = true
        }.InitTemplate();
    }

    public async Task<bool> Start(CancellationToken ct)
    {
        if (CaptureToRectArea().Find(_trackPoint).IsEmpty())
        {
            Logger.LogInformation("未找到追踪点，停止任务");
            throw new Exception("未找到追踪点");
        }

        return await MakeTrackPointDirectlyAbove(ct);
    }

    private async Task<bool> MakeTrackPointDirectlyAbove(CancellationToken ct)
    {
        try
        {
            double dpi = TaskContext.Instance().DpiScale;
            var startTime = DateTime.Now;
            int prevMoveX = 0;
            while (!ct.IsCancellationRequested)
            {
                var ra = CaptureToRectArea();
                var trackPointRa = ra.Find(_trackPoint);
                if (trackPointRa.IsExist())
                {
                    // 使追踪点位于俯视角上方
                    var centerY = trackPointRa.Y + trackPointRa.Height / 2;
                    if (centerY > CaptureRect.Height / 2)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(-50, 0);
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

                        Debug.WriteLine("使追踪点位于俯视角上方");
                        continue;
                    }

                    // 调整方向
                    var centerX = trackPointRa.X + trackPointRa.Width / 2;
                    var moveX = (int)((centerX - CaptureRect.Width / 2.0) / 8 / dpi);
                    moveX = moveX switch
                    {
                        >= 10 and < 50 => 80 + moveX,
                        > -50 and <= -10 => -80 + moveX,
                        > 0 and < 10 => 10 + moveX,
                        > -10 and < 0 => -10 + moveX,
                        _ => moveX
                    };
                    if (moveX != 0)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(moveX, 0);
                        Debug.WriteLine("调整方向:" + moveX);
                    }

                    if (moveX == 0 || prevMoveX * moveX < 0)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    }
                    else
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    }

                    // 识别F
                    var text = Bv.FindFKeyText(ra);
                    if (!string.IsNullOrEmpty(text) && text.Contains("激活"))
                    {
                        Logger.LogInformation("追踪：识别到[{Msg}]", text);
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        return true;
                    }

                    prevMoveX = moveX;
                }
                else
                {
                    // 随机移动
                    Logger.LogInformation("未找到追踪目标");
                }

                if (DateTime.Now - startTime > TimeSpan.FromMilliseconds(_timeoutMilliseconds))
                {
                    Logger.LogInformation("追踪超时");
                    return false;
                }

                Simulation.SendInput.Mouse.MoveMouseBy(0, 800); // 保证俯视角（低头）
                await Delay(100, ct);
            }

            return false;
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }
}