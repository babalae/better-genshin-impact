using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class BurstSymbol : InstructionSymbol, IInstructionSymbolHasAlias
{
    public BurstSymbol(bool isAlias, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("burst", parameterList, leadingTriviaList, tailingTrivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [0]);

        IsAlias = isAlias;
    }

    public BurstSymbol(bool isAlias, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("burst", leadingTriviaList, tailingTrivia)
    {
        IsAlias = isAlias;
    }

    public string AliasName { get; } = "q";

    public bool IsAlias { get; }
}