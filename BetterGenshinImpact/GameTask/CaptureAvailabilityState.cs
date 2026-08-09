namespace BetterGenshinImpact.GameTask;

internal sealed record CaptureAvailabilityState(
    bool CanProcessFrame,
    bool IsGameActive,
    bool BackgroundTriggersOnly,
    bool ShouldUpdatePictureInPicture)
{
    public static CaptureAvailabilityState Unavailable { get; } = new(false, false, false, false);
}

internal readonly record struct TriggerActivityState(
    bool HasEnabledTriggers,
    bool HasBackgroundTriggerToRun);
