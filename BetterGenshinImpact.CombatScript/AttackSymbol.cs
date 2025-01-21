using System;
using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class AttackSymbol : InstructionSymbol, IInstructionSymbolHasDuration
{
    public AttackSymbol(ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> trivia)
        : base("attack", parameterList, trivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [0, 1]);

        if (parameterList.Length is 1)
        {
            InstructionThrowHelper.ThrowIfParameterAtIndexIsNot(parameterList, 0, out DoubleSymbol doubleSymbol);

            HasDuration = true;
            Duration = TimeSpan.FromSeconds(doubleSymbol.Value);
        }
    }

    public AttackSymbol(ImmutableArray<TriviaSymbol> trivia)
        : base("attack", trivia)
    {
    }

    public bool HasDuration { get; }

    public TimeSpan Duration { get; }
}