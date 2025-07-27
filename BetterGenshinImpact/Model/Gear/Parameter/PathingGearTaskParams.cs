using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Model.Gear.Parameter;

public class PathingGearTaskParams
{
    public string Path { get; set; } = string.Empty;

    public PathingPartyConfig? PathingPartyConfig { get; set; }
}