using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Hdr;

public static class HdrDetectionEvaluator
{
    public static HdrDetectionResult Evaluate(
        bool isGameHdrKnown,
        bool gameHdrEnabled,
        bool isAutoHdrKnown,
        AutoHdrState appAutoHdrState,
        AutoHdrState globalAutoHdrState,
        bool isDisplayHdrKnown,
        DisplayHdrState displayHdrState,
        string? gameExePath,
        string? autoHdrUnknownReason = null,
        string? gameHdrUnknownReason = null,
        string? displayHdrUnknownReason = null)
    {
        AutoHdrState effectiveAutoHdrState = isAutoHdrKnown
            ? UserGpuPreferencesParser.ResolveEffectiveState(appAutoHdrState, globalAutoHdrState)
            : AutoHdrState.Unknown;

        bool effectiveAutoHdrEnabled = effectiveAutoHdrState == AutoHdrState.Enabled;
        bool displayHdrEnabled = isDisplayHdrKnown && displayHdrState == DisplayHdrState.Enabled;
        HdrRiskLevel riskLevel;

        if (gameHdrEnabled || effectiveAutoHdrEnabled || displayHdrEnabled)
        {
            riskLevel = HdrRiskLevel.Risky;
        }
        else if (!isGameHdrKnown || !isAutoHdrKnown || !isDisplayHdrKnown)
        {
            riskLevel = HdrRiskLevel.Unknown;
        }
        else
        {
            riskLevel = HdrRiskLevel.Safe;
        }

        return new HdrDetectionResult
        {
            RiskLevel = riskLevel,
            GameHdrEnabled = gameHdrEnabled,
            IsGameHdrKnown = isGameHdrKnown,
            AppAutoHdrState = isAutoHdrKnown ? appAutoHdrState : AutoHdrState.Unknown,
            GlobalAutoHdrState = isAutoHdrKnown ? globalAutoHdrState : AutoHdrState.Unknown,
            AutoHdrState = effectiveAutoHdrState,
            IsAutoHdrKnown = isAutoHdrKnown,
            EffectiveAutoHdrEnabled = effectiveAutoHdrEnabled,
            DisplayHdrState = isDisplayHdrKnown ? displayHdrState : DisplayHdrState.Unknown,
            IsDisplayHdrKnown = isDisplayHdrKnown,
            DisplayHdrEnabled = displayHdrEnabled,
            ReasonText = BuildReasonText(
                riskLevel,
                isGameHdrKnown,
                gameHdrEnabled,
                isAutoHdrKnown,
                effectiveAutoHdrEnabled,
                isDisplayHdrKnown,
                displayHdrEnabled,
                autoHdrUnknownReason,
                gameHdrUnknownReason,
                displayHdrUnknownReason),
            GameExePath = gameExePath,
        };
    }

    private static string BuildReasonText(
        HdrRiskLevel riskLevel,
        bool isGameHdrKnown,
        bool gameHdrEnabled,
        bool isAutoHdrKnown,
        bool effectiveAutoHdrEnabled,
        bool isDisplayHdrKnown,
        bool displayHdrEnabled,
        string? autoHdrUnknownReason,
        string? gameHdrUnknownReason,
        string? displayHdrUnknownReason)
    {
        if (riskLevel == HdrRiskLevel.Risky)
        {
            List<string> riskSources = [];
            if (gameHdrEnabled)
            {
                riskSources.Add("游戏内 HDR");
            }

            if (effectiveAutoHdrEnabled)
            {
                riskSources.Add("Windows Auto HDR");
            }

            if (displayHdrEnabled)
            {
                riskSources.Add("Windows 显示 HDR / Advanced Color");
            }

            string prefix = riskSources.Count > 0
                ? $"检测到{string.Join("、", riskSources)}已开启。"
                : "检测到 HDR 风险状态。";

            return prefix + "当前 HDR 处于开启状态，默认截图模式下颜色识别可能异常，自动战斗、秘境和一条龙等功能更容易失效。";
        }

        if (riskLevel == HdrRiskLevel.Safe)
        {
            return "未检测到游戏内 HDR、Windows Auto HDR 或系统显示 HDR，当前 HDR 处于关闭状态。默认截图模式通常可继续使用。";
        }

        List<string> reasons = [];
        if (!isGameHdrKnown)
        {
            reasons.Add(gameHdrUnknownReason ?? "无法读取游戏内 HDR 配置");
        }

        if (!isAutoHdrKnown)
        {
            reasons.Add(autoHdrUnknownReason ?? "无法判断 Windows Auto HDR 状态");
        }

        if (!isDisplayHdrKnown)
        {
            reasons.Add(displayHdrUnknownReason ?? "无法判断系统显示 HDR 状态");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("HDR 状态无法完整判断");
        }

        return string.Join("；", reasons) + "。当前无法完整判断 HDR 是否开启；如果继续执行后出现颜色异常、识别失败或任务不稳定，建议切换到 WindowsGraphicsCapture（HDR）或手动关闭 HDR。";
    }
}
