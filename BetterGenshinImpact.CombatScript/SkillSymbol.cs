using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class SkillSymbol : InstructionSymbol, IInstructionSymbolHasAlias
{
    public SkillSymbol(bool isAlias, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> trivia)
        : base("skill", parameterList, trivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [0, 1]);

        if (parameterList.Length is 1)
        {
            InstructionThrowHelper.ThrowIfParameterAtIndexIsNot(parameterList, 0, out HoldSymbol _);
        }

        IsAlias = isAlias;
    }

    public SkillSymbol(bool isAlias, ImmutableArray<TriviaSymbol> trivia)
        : base("skill", trivia)
    {
        IsAlias = isAlias;
    }

    public string AliasName { get; } = "e";

    public bool IsAlias { get; }
}