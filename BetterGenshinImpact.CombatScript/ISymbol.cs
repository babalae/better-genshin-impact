namespace BetterGenshinImpact.CombatScript;

public interface ISymbol
{
    void Emit(ISymbolEmitter emitter);
}