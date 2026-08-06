using BetterGenshinImpact.GameTask.Model.Area;

namespace BetterGenshinImpact.GameTask.Common.Party;

/// <summary>
/// Minimal party capability required by an avatar to determine the active slot.
/// </summary>
public interface IAvatarPartyContext
{
    int GetActiveAvatarIndex(ImageRegion imageRegion, AvatarActiveCheckContext context);
}
