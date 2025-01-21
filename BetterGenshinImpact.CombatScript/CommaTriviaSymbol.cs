namespace BetterGenshinImpact.CombatScript;

public sealed class CommaTriviaSymbol : TriviaSymbol
{
    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(',');
    }
}

public sealed class SemicolonTriviaSymbol : TriviaSymbol
{
    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(';');
    }
}