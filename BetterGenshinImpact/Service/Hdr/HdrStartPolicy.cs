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
                    CanSwitchToBitBlt = true,
                    CanOpenGraphicsSettings = false,
                    ContinueIsPrimary = false,
                    Title = "HDR 未开启",
                    Message = $"{detectionResult.ReasonText}\n\n当前使用的是 HDR 截图模式，通常建议切回 BitBlt。\n如果你是有意这样设置，也可以继续。",
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

        string switchHint = hdrCaptureSupported
            ? "建议先切到 WindowsGraphicsCapture（HDR）。"
            : "当前系统不支持 HDR 截图模式，建议先关闭 HDR。";

        string impactHint = purpose == HdrStartPurpose.RealtimeOnly
            ? "继续后可能出现颜色异常或识别不稳。"
            : "继续后可能出现识别失败、任务中断或结果不稳定。";

        return new HdrStartDecision
        {
            ShouldWarn = true,
            CanSwitchToHdrCapture = hdrCaptureSupported,
            CanSwitchToBitBlt = false,
            CanOpenGraphicsSettings = true,
            Title = detectionResult.RiskLevel == HdrRiskLevel.Risky ? "HDR 已开启" : "HDR 状态未知",
            Message = $"{detectionResult.ReasonText}\n\n{switchHint}\n{impactHint}",
        };
    }
}
