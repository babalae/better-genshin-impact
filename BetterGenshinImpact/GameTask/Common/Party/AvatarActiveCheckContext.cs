namespace BetterGenshinImpact.GameTask.Common.Party;

/// <summary>
/// Accumulates active-party-slot recognition results across repeated checks.
/// </summary>
public class AvatarActiveCheckContext
{
    public int[] ActiveIndexByArrowCount { get; set; } = new int[4];

    public int TotalCheckFailedCount { get; set; }
}
