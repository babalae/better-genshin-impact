namespace BetterGenshinImpact.Model;

public sealed class UpdateOption
{
    public UpdateTrigger Trigger { get; set; } = default;
}

public enum UpdateTrigger
{
    Auto,
    Manual,
}
