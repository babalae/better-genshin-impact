using BetterGenshinImpact.Service.Hdr;
using Fischless.GameCapture;

namespace BetterGenshinImpact.UnitTest.ServiceTests.HdrTests;

public class UserGpuPreferencesParserTests
{
    [Theory]
    [InlineData("AutoHDREnable=2097", AutoHdrState.Enabled)]
    [InlineData("OtherSetting=1; AutoHDREnable=2096", AutoHdrState.Disabled)]
    [InlineData("OtherSetting=1", AutoHdrState.Unset)]
    [InlineData("AutoHDREnable", AutoHdrState.Unset)]
    [InlineData("AutoHDREnable=1234", AutoHdrState.Unknown)]
    public void GetAutoHdrState_ShouldReturnExpectedState(string rawValue, AutoHdrState expected)
    {
        AutoHdrState result = UserGpuPreferencesParser.GetAutoHdrState(rawValue);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(AutoHdrState.Enabled, AutoHdrState.Disabled, AutoHdrState.Enabled)]
    [InlineData(AutoHdrState.Disabled, AutoHdrState.Enabled, AutoHdrState.Disabled)]
    [InlineData(AutoHdrState.Unset, AutoHdrState.Enabled, AutoHdrState.Enabled)]
    [InlineData(AutoHdrState.Unset, AutoHdrState.Disabled, AutoHdrState.Disabled)]
    [InlineData(AutoHdrState.Unknown, AutoHdrState.Enabled, AutoHdrState.Unknown)]
    public void ResolveEffectiveState_ShouldHonorAppOverride(
        AutoHdrState appState,
        AutoHdrState globalState,
        AutoHdrState expected)
    {
        AutoHdrState result = UserGpuPreferencesParser.ResolveEffectiveState(appState, globalState);

        Assert.Equal(expected, result);
    }
}

public class HdrDetectionEvaluatorTests
{
    [Fact]
    public void Evaluate_ShouldMarkRisky_WhenGameHdrEnabled()
    {
        HdrDetectionResult result = HdrDetectionEvaluator.Evaluate(
            isGameHdrKnown: true,
            gameHdrEnabled: true,
            isAutoHdrKnown: true,
            appAutoHdrState: AutoHdrState.Unset,
            globalAutoHdrState: AutoHdrState.Disabled,
            isDisplayHdrKnown: true,
            displayHdrState: DisplayHdrState.Disabled,
            gameExePath: @"C:\Games\Genshin Impact Game\YuanShen.exe");

        Assert.Equal(HdrRiskLevel.Risky, result.RiskLevel);
        Assert.True(result.GameHdrEnabled);
        Assert.False(result.EffectiveAutoHdrEnabled);
    }

    [Fact]
    public void Evaluate_ShouldMarkRisky_WhenAutoHdrEnabled()
    {
        HdrDetectionResult result = HdrDetectionEvaluator.Evaluate(
            isGameHdrKnown: true,
            gameHdrEnabled: false,
            isAutoHdrKnown: true,
            appAutoHdrState: AutoHdrState.Enabled,
            globalAutoHdrState: AutoHdrState.Disabled,
            isDisplayHdrKnown: true,
            displayHdrState: DisplayHdrState.Disabled,
            gameExePath: @"C:\Games\Genshin Impact Game\YuanShen.exe");

        Assert.Equal(HdrRiskLevel.Risky, result.RiskLevel);
        Assert.True(result.EffectiveAutoHdrEnabled);
        Assert.Equal(AutoHdrState.Enabled, result.AutoHdrState);
    }

    [Fact]
    public void Evaluate_ShouldTreatAppDisabledAsOverride_WhenGlobalEnabled()
    {
        HdrDetectionResult result = HdrDetectionEvaluator.Evaluate(
            isGameHdrKnown: true,
            gameHdrEnabled: false,
            isAutoHdrKnown: true,
            appAutoHdrState: AutoHdrState.Disabled,
            globalAutoHdrState: AutoHdrState.Enabled,
            isDisplayHdrKnown: true,
            displayHdrState: DisplayHdrState.Disabled,
            gameExePath: @"C:\Games\Genshin Impact Game\YuanShen.exe");

        Assert.Equal(HdrRiskLevel.Safe, result.RiskLevel);
        Assert.False(result.EffectiveAutoHdrEnabled);
        Assert.Equal(AutoHdrState.Disabled, result.AutoHdrState);
    }

    [Fact]
    public void Evaluate_ShouldReturnUnknown_WhenAutoHdrCannotBeDetermined()
    {
        HdrDetectionResult result = HdrDetectionEvaluator.Evaluate(
            isGameHdrKnown: true,
            gameHdrEnabled: false,
            isAutoHdrKnown: false,
            appAutoHdrState: AutoHdrState.Unknown,
            globalAutoHdrState: AutoHdrState.Unknown,
            isDisplayHdrKnown: true,
            displayHdrState: DisplayHdrState.Disabled,
            gameExePath: null,
            autoHdrUnknownReason: "Game path missing");

        Assert.Equal(HdrRiskLevel.Unknown, result.RiskLevel);
        Assert.False(result.IsAutoHdrKnown);
        Assert.Equal(AutoHdrState.Unknown, result.AutoHdrState);
        Assert.False(result.EffectiveAutoHdrEnabled);
    }

    [Fact]
    public void Evaluate_ShouldMarkRisky_WhenSystemDisplayHdrEnabled()
    {
        HdrDetectionResult result = HdrDetectionEvaluator.Evaluate(
            isGameHdrKnown: true,
            gameHdrEnabled: false,
            isAutoHdrKnown: true,
            appAutoHdrState: AutoHdrState.Unset,
            globalAutoHdrState: AutoHdrState.Disabled,
            isDisplayHdrKnown: true,
            displayHdrState: DisplayHdrState.Enabled,
            gameExePath: @"C:\Games\Genshin Impact Game\YuanShen.exe");

        Assert.Equal(HdrRiskLevel.Risky, result.RiskLevel);
        Assert.True(result.DisplayHdrEnabled);
        Assert.Equal(DisplayHdrState.Enabled, result.DisplayHdrState);
    }

    [Fact]
    public void Evaluate_ShouldReturnUnknown_WhenDisplayHdrCannotBeDetermined()
    {
        HdrDetectionResult result = HdrDetectionEvaluator.Evaluate(
            isGameHdrKnown: true,
            gameHdrEnabled: false,
            isAutoHdrKnown: true,
            appAutoHdrState: AutoHdrState.Unset,
            globalAutoHdrState: AutoHdrState.Disabled,
            isDisplayHdrKnown: false,
            displayHdrState: DisplayHdrState.Unknown,
            gameExePath: @"C:\Games\Genshin Impact Game\YuanShen.exe",
            displayHdrUnknownReason: "Display mapping failed");

        Assert.Equal(HdrRiskLevel.Unknown, result.RiskLevel);
        Assert.False(result.IsDisplayHdrKnown);
        Assert.False(result.DisplayHdrEnabled);
    }
}

public class HdrStartPolicyTests
{
    [Fact]
    public void Evaluate_ShouldWarnForRealtimeRiskyStart_WhenNotUsingHdrCapture()
    {
        HdrStartDecision result = HdrStartPolicy.Evaluate(
            CreateDetectionResult(HdrRiskLevel.Risky),
            CaptureModes.BitBlt.ToString(),
            HdrStartPurpose.RealtimeOnly,
            hdrCaptureSupported: true);

        Assert.True(result.ShouldWarn);
        Assert.True(result.CanSwitchToHdrCapture);
        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Evaluate_ShouldWarnForStandaloneRiskyStart_WhenNotUsingHdrCapture()
    {
        HdrStartDecision result = HdrStartPolicy.Evaluate(
            CreateDetectionResult(HdrRiskLevel.Risky),
            CaptureModes.BitBlt.ToString(),
            HdrStartPurpose.StandaloneOrAutomation,
            hdrCaptureSupported: true);

        Assert.True(result.ShouldWarn);
        Assert.True(result.CanSwitchToHdrCapture);
    }

    [Fact]
    public void Evaluate_ShouldNotWarn_WhenHdrCaptureModeIsAlreadyEnabled()
    {
        HdrStartDecision result = HdrStartPolicy.Evaluate(
            CreateDetectionResult(HdrRiskLevel.Risky),
            CaptureModes.WindowsGraphicsCaptureHdr.ToString(),
            HdrStartPurpose.StandaloneOrAutomation,
            hdrCaptureSupported: true);

        Assert.False(result.ShouldWarn);
        Assert.False(result.CanSwitchToHdrCapture);
    }

    [Fact]
    public void Evaluate_ShouldDisableSwitchAction_WhenHdrCaptureIsNotSupported()
    {
        HdrStartDecision result = HdrStartPolicy.Evaluate(
            CreateDetectionResult(HdrRiskLevel.Unknown),
            CaptureModes.BitBlt.ToString(),
            HdrStartPurpose.RealtimeOnly,
            hdrCaptureSupported: false);

        Assert.True(result.ShouldWarn);
        Assert.False(result.CanSwitchToHdrCapture);
    }

    [Fact]
    public void Evaluate_ShouldNotWarn_WhenRiskIsSafe()
    {
        HdrStartDecision result = HdrStartPolicy.Evaluate(
            CreateDetectionResult(HdrRiskLevel.Safe),
            CaptureModes.BitBlt.ToString(),
            HdrStartPurpose.StandaloneOrAutomation,
            hdrCaptureSupported: true);

        Assert.False(result.ShouldWarn);
        Assert.False(result.CanSwitchToHdrCapture);
    }

    private static HdrDetectionResult CreateDetectionResult(HdrRiskLevel riskLevel)
    {
        return new HdrDetectionResult
        {
            RiskLevel = riskLevel,
            ReasonText = "HDR detection result",
        };
    }
}
