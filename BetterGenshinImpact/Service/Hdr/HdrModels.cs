namespace BetterGenshinImpact.Service.Hdr;

public enum HdrRiskLevel
{
    Safe,
    Risky,
    Unknown,
}

public enum AutoHdrState
{
    Unknown,
    Unset,
    Disabled,
    Enabled,
}

public enum DisplayHdrState
{
    Unknown,
    Disabled,
    Enabled,
}

public enum HdrStartPurpose
{
    RealtimeOnly,
    StandaloneOrAutomation,
}

public sealed class HdrDetectionResult
{
    public HdrRiskLevel RiskLevel { get; init; }

    public bool GameHdrEnabled { get; init; }

    public bool IsGameHdrKnown { get; init; }

    public AutoHdrState AutoHdrState { get; init; }

    public AutoHdrState AppAutoHdrState { get; init; }

    public AutoHdrState GlobalAutoHdrState { get; init; }

    public bool IsAutoHdrKnown { get; init; }

    public bool EffectiveAutoHdrEnabled { get; init; }

    public DisplayHdrState DisplayHdrState { get; init; }

    public bool IsDisplayHdrKnown { get; init; }

    public bool DisplayHdrEnabled { get; init; }

    public string ReasonText { get; init; } = string.Empty;

    public string? GameExePath { get; init; }
}

public sealed class HdrStartDecision
{
    public bool ShouldWarn { get; init; }

    public bool CanSwitchToHdrCapture { get; init; }

    public bool CanSwitchToBitBlt { get; init; }

    public bool CanOpenGraphicsSettings { get; init; } = true;

    public bool ContinueIsPrimary { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
