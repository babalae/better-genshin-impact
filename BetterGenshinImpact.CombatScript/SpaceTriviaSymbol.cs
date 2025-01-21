namespace BetterGenshinImpact.CombatScript;

public class SpaceTriviaSymbol : TriviaSymbol
{
    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(' ');
    }
}