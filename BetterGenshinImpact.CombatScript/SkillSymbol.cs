using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class SkillSymbol : InstructionSymbol, IInstructionSymbolHasAlias
{
    public SkillSymbol(bool isAlias, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("skill", parameterList, leadingTriviaList, tailingTrivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [0, 1]);

        if (parameterList.Length is 1)
        {
            InstructionThrowHelper.ThrowIfParameterAtIndexIsNot(parameterList, 0, out HoldSymbol _);
        }

        IsAlias = isAlias;
    }

    public SkillSymbol(bool isAlias, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("skill", leadingTriviaList, tailingTrivia)
    {
        IsAlias = isAlias;
    }

    public string AliasName { get; } = "e";

    public bool IsAlias { get; }
}