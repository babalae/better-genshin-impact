using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class BurstSymbol : InstructionSymbol, IInstructionSymbolHasAlias
{
    public BurstSymbol(bool isAlias, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> trivia)
        : base("burst", parameterList, trivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [0]);

        IsAlias = isAlias;
    }

    public BurstSymbol(bool isAlias, ImmutableArray<TriviaSymbol> trivia)
        : base("burst", trivia)
    {
        IsAlias = isAlias;
    }

    public string AliasName { get; } = "q";

    public bool IsAlias { get; }
}