namespace BetterGenshinImpact.CombatScript;

public sealed class HoldSymbol : BaseSymbol, IParameterSymbol
{
    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append("hold");
    }
}