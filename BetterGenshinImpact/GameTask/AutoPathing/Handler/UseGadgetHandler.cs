using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发使用小道具
/// </summary>
public class UseGadgetHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation(Lang.S["GameTask_11069_7cad94"], "使用小道具");
        Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);


        if (waypointForTrack != null
            && !string.IsNullOrEmpty(waypointForTrack.ActionParams)
            && waypointForTrack.ActionParams.Contains("not_wait", StringComparison.OrdinalIgnoreCase))
        {
            // 不等待
            Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
        }
        else
        {
            double maxWaitSeconds = 100;
            if (waypointForTrack != null
                && !string.IsNullOrEmpty(waypointForTrack.ActionParams))
            {
                double.TryParse(waypointForTrack.ActionParams, out maxWaitSeconds); // 最大等待时间，单位秒
            }

            var screen = CaptureToRectArea();
            var cd = GetCurrentCd(screen);
            if (cd > 100)
            {
                Logger.LogWarning(Lang.S["GameTask_11156_f3ca37"], cd);
                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
            }
            else if (cd > 0)
            {
                Logger.LogInformation(Lang.S["GameTask_11155_d78a0c"], cd);
                // 等待小道具CD结束
                int waitTime; // 等待CD结束后再继续
                if (cd > maxWaitSeconds)
                {
                    waitTime = (int)(maxWaitSeconds * 1000);
                    Logger.LogInformation(Lang.S["GameTask_11154_730a72"], maxWaitSeconds);
                }
                else
                {
                    waitTime = (int)(cd * 1000) + 100; // 等待CD结束后再继续
                }

                await Delay(waitTime, ct);
                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
            }
            else
            {
                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
            }
        }

        Logger.LogInformation(Lang.S["GameTask_11153_b4d881"]);
        await Delay(300, ct);
    }

    /// <summary>
    /// 小道具是否正在CD中
    /// 77x77
    /// </summary>
    private double GetCurrentCd(ImageRegion imageRegion)
    {
        var eRa = imageRegion.DeriveCrop(AutoFightAssets.Instance.ZCooldownRect);
        var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        return StringUtils.TryParseDouble(text);
    }
}