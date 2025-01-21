namespace BetterGenshinImpact.CombatScript;

public class SpaceTriviaSymbol : TriviaSymbol
{
    private readonly int count;
    
    public SpaceTriviaSymbol()
    {
        count = 1;
    }

    public SpaceTriviaSymbol(int count)
    {
        this.count = count;
    }
    
    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(' ', count);
    }
}