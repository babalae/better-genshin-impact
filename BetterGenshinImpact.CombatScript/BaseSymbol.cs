namespace BetterGenshinImpact.CombatScript;

public abstract class BaseSymbol : ISymbol
{
    public abstract void Emit(ISymbolEmitter emitter);
}