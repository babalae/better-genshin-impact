using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 处理使用小道具 (如四叶印等) 动作的逻辑 / Handles the execution logic for quick-use gadgets.
/// 支持基于视觉的当前冷却时间 (CD) 推算与等待 / Supports visual-based CD checking and conditional waiting.
/// </summary>
public class UseGadgetHandler : IActionHandler
{
    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【使用小道具】 / Executing action: [Use Gadget]");
        Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);

        var actionParams = waypointForTrack?.ActionParams ?? string.Empty;

        if (actionParams.Contains("not_wait", StringComparison.OrdinalIgnoreCase))
        {
            Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
            await Delay(300, ct);
            return;
        }

        double maxWaitSeconds = 100;
        if (!string.IsNullOrWhiteSpace(actionParams))
        {
            double.TryParse(actionParams, out maxWaitSeconds);
        }

        var screen = CaptureToRectArea();
        var cd = GetCurrentCd(screen);

        if (cd > 100)
        {
            Logger.LogWarning("小道具CD读取值异常: {Cd}秒。可能为识别错误，将强制跳过等待 / Gadget CD anomaly read. Skipping wait.", cd);
            Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
        }
        else if (cd > 0)
        {
            Logger.LogInformation("小道具正在冷却中，需等待：{Cd}秒 / Gadget in CD, waiting...", cd);
            
            var waitTimeMs = cd > maxWaitSeconds 
                ? (int)(maxWaitSeconds * 1000) 
                : (int)(cd * 1000) + 100;

            if (cd > maxWaitSeconds)
            {
                Logger.LogDebug("CD 超过最大允许时限，截断等待时长为: {Max}秒 / Truncating wait to max: {Max}s", maxWaitSeconds);
            }

            await Delay(waitTimeMs, ct);
            Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
        }
        else
        {
            Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
        }

        Logger.LogInformation("已完成小道具使用 / Completed gadget usage.");
        await Delay(300, ct);
    }

    /// <summary>
    /// 读取屏幕区域并OCR识别小道具当前CD值。 / OCR-based mechanism to extract the current Gadget CD.
    /// </summary>
    private double GetCurrentCd(ImageRegion imageRegion)
    {
        var eRa = imageRegion.DeriveCrop(AutoFightAssets.Instance.ZCooldownRect);
        
        using var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        
        return StringUtils.TryParseDouble(text);
    }
}
