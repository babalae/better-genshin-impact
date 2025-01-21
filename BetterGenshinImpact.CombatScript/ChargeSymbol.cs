using System;
using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class ChargeSymbol : InstructionSymbol, IInstructionSymbolHasDuration
{
    public ChargeSymbol(ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> trivia)
        : base("charge", parameterList, trivia)
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

    public ChargeSymbol(ImmutableArray<TriviaSymbol> trivia)
        : base("charge", trivia)
    {
    }

    public bool HasDuration { get; }

    public TimeSpan Duration { get; }
}