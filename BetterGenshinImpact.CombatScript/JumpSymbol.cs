using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class JumpSymbol : InstructionSymbol, IInstructionSymbolHasAlias
{
    public JumpSymbol(bool isAlias, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> trivia)
        : base("jump", parameterList, trivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [0]);

        IsAlias = isAlias;
    }

    public JumpSymbol(bool isAlias, ImmutableArray<TriviaSymbol> trivia)
        : base("jump", trivia)
    {
        IsAlias = isAlias;
    }

    public string AliasName { get; } = "j";

    public bool IsAlias { get; }
}