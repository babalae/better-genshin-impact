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
                riskSources.Add("系统显示 HDR");
            }

            return riskSources.Count > 0
                ? $"来源：{string.Join("、", riskSources)}。"
                : "检测到 HDR 风险。";
        }

        if (riskLevel == HdrRiskLevel.Safe)
        {
            return "未检测到游戏内 HDR、Windows Auto HDR 或系统显示 HDR。";
        }

        List<string> reasons = [];
        if (!isGameHdrKnown)
        {
            reasons.Add(gameHdrUnknownReason ?? "无法读取游戏内 HDR 设置");
        }

        if (!isAutoHdrKnown)
        {
            reasons.Add(autoHdrUnknownReason ?? "无法判断 Windows Auto HDR");
        }

        if (!isDisplayHdrKnown)
        {
            reasons.Add(displayHdrUnknownReason ?? "无法判断系统显示 HDR");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("HDR 状态未知");
        }

        return string.Join("；", reasons) + "。";
    }
}
