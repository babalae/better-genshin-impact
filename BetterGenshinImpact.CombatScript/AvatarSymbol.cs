namespace BetterGenshinImpact.CombatScript;

public sealed class AvatarSymbol : BaseSymbol
{
    public AvatarSymbol(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(Name);
    }
}