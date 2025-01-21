using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public static class SymbolEmitterExtensions
{
    public static ISymbolEmitter Append(this ISymbolEmitter emitter, ISymbol symbol)
    {
        symbol.Emit(emitter);
        return emitter;
    }

    public static ISymbolEmitter Append<TSymbol>(this ISymbolEmitter emitter, ImmutableArray<TSymbol> symbolList)
        where TSymbol : ISymbol
    {
        foreach(ref readonly TSymbol symbol in symbolList.AsSpan())
        {
            symbol.Emit(emitter);
        }

        return emitter;
    }
}