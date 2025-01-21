using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public sealed class ScriptUnit
{
    public ScriptUnit(ImmutableArray<ISymbol> symbols)
    {
        Symbols = symbols;
    }

    public ImmutableArray<ISymbol> Symbols { get; }

    public string Emit(ISymbolEmitter emitter)
    {
        foreach (ref readonly ISymbol symbol in Symbols.AsSpan())
        {
            symbol.Emit(emitter);
        }

        return emitter.Emit();
    }
}