using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Helpers;
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
    private RECT CaptureRect => TaskContext.Instance().SystemInfo.CaptureAreaRect;

    private double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    private readonly RecognitionObject _trackPoint;

    private int _timeoutMilliseconds;


    public LowerHeadThenWalkToTask(string targetMatName, int timeoutMilliseconds = 30000)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
        _trackPoint = new RecognitionObject
        {
            Name = "BlueTrackPoint",
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
        var startTime = DateTime.Now;
        int prevMoveX = 0;
        bool wDown = false;
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
                    if (wDown)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        wDown = false;
                    }

                    Debug.WriteLine("使追踪点位于俯视角上方");
                    continue;
                }

                // 调整方向
                var centerX = trackPointRa.X + trackPointRa.Width / 2;
                var moveX = (centerX - CaptureRect.Width / 2) / 8;
                moveX = moveX switch
                {
                    > 0 and < 10 => 10,
                    > -10 and < 0 => -10,
                    _ => moveX
                };
                if (moveX != 0)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(moveX, 0);
                    Debug.WriteLine("调整方向:" + moveX);
                }

                if (moveX == 0 || prevMoveX * moveX < 0)
                {
                    if (!wDown)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                        wDown = true;
                    }
                }

                if (Math.Abs(moveX) < 50 && Math.Abs(centerY - CaptureRect.Height / 2) < 200)
                {
                    if (wDown)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        wDown = false;
                    }

                    // 识别F
                    var res = ra.Find(AutoPickAssets.Instance.PickRo);
                    if (res.IsExist())
                    {
                        Logger.LogInformation("追踪：识别到[F]");
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        return true;
                    }

                    Logger.LogInformation("追踪：到达目标");
                    break;
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

            Simulation.SendInput.Mouse.MoveMouseBy(0, 500); // 保证俯视角（低头）
            await Delay(100, ct);
        }

        return false;
    }
}