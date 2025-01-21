using System;
using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class DashSymbol : InstructionSymbol, IInstructionSymbolHasDuration
{
    public DashSymbol(ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("dash", parameterList, leadingTriviaList, tailingTrivia)
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

    public DashSymbol(ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("dash", leadingTriviaList, tailingTrivia)
    {
    }

    public bool HasDuration { get; }

    public TimeSpan Duration { get; }
}