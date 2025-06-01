namespace BetterGenshinImpact.Model;

public sealed class UpdateOption
{
    public UpdateTrigger Trigger { get; set; } = default;
    
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
}

public enum UpdateTrigger
{
    Auto,
    Manual,
}


public enum UpdateChannel
{
    Stable,
    Alpha,
}