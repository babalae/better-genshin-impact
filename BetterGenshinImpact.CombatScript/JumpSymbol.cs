using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class JumpSymbol : InstructionSymbol, IInstructionSymbolHasAlias
{
    public JumpSymbol(bool isAlias, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("jump", parameterList, leadingTriviaList, tailingTrivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [0]);

        IsAlias = isAlias;
    }

    public JumpSymbol(bool isAlias, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("jump", leadingTriviaList, tailingTrivia)
    {
        IsAlias = isAlias;
    }

    public string AliasName { get; } = "j";

    public bool IsAlias { get; }
}