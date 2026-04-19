using System;
using Fischless.GameCapture;

namespace BetterGenshinImpact.Service.Hdr;

public static class HdrStartPolicy
{
    public static HdrStartDecision Evaluate(
        HdrDetectionResult detectionResult,
        string? captureMode,
        HdrStartPurpose purpose,
        bool hdrCaptureSupported)
    {
        bool usingHdrCaptureMode = string.Equals(
            captureMode,
            CaptureModes.WindowsGraphicsCaptureHdr.ToString(),
            StringComparison.Ordinal);

        if (usingHdrCaptureMode)
        {
            if (detectionResult.RiskLevel == HdrRiskLevel.Safe)
            {
                return new HdrStartDecision
                {
                    ShouldWarn = true,
                    CanSwitchToHdrCapture = false,
                    CanOpenGraphicsSettings = false,
                    ContinueIsPrimary = true,
                    Title = "当前未开启 HDR",
                    Message = $"{detectionResult.ReasonText}\n\n你当前选择的是 WindowsGraphicsCapture（HDR），但现在没有检测到 HDR 已开启。这通常不是必须的；如果只是普通 SDR 环境，建议改回普通截图模式后再启动。\n\n如果你是有意这样配置，也可以继续执行。",
                };
            }

            return new HdrStartDecision
            {
                ShouldWarn = false,
                CanSwitchToHdrCapture = false,
            };
        }

        if (detectionResult.RiskLevel == HdrRiskLevel.Safe)
        {
            return new HdrStartDecision
            {
                ShouldWarn = false,
                CanSwitchToHdrCapture = false,
            };
        }

        string scopeWarning = purpose == HdrStartPurpose.RealtimeOnly
            ? "实时任务里部分功能仍可能可用，但识别颜色会变得不稳定。"
            : "独立任务、自动战斗、秘境和一条龙受影响更大，可能出现识别失败、无法切队、战斗无法正常结束或路径执行异常。";

        string switchHint = hdrCaptureSupported
            ? "建议优先切换到 WindowsGraphicsCapture（HDR）后再启动。"
            : "当前系统不支持 WindowsGraphicsCapture（HDR），建议手动关闭游戏内 HDR 或 Windows Auto HDR。";

        string unknownHint = detectionResult.RiskLevel == HdrRiskLevel.Unknown
            ? "当前无法完整判断 HDR 是否开启。"
            : "当前检测到 HDR 处于开启状态。";

        return new HdrStartDecision
        {
            ShouldWarn = true,
            CanSwitchToHdrCapture = hdrCaptureSupported,
            CanOpenGraphicsSettings = true,
            Title = detectionResult.RiskLevel == HdrRiskLevel.Risky ? "检测到 HDR 已开启" : "HDR 状态未知",
            Message = $"{unknownHint}\n\n{detectionResult.ReasonText}\n\n{scopeWarning}\n{switchHint}\n\n如果仍选择继续执行，可能出现颜色异常、识别失败、任务中断或结果不稳定。",
        };
    }
}
