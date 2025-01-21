using System;
using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class WaitSymbol : InstructionSymbol, IInstructionSymbolHasDuration
{
    public WaitSymbol(ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> trivia)
        : base("wait", parameterList, trivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [1]);

        InstructionThrowHelper.ThrowIfParameterAtIndexIsNot(parameterList, 0, out DoubleSymbol doubleSymbol);

        HasDuration = true;
        Duration = TimeSpan.FromSeconds(doubleSymbol.Value);
    }

    public bool HasDuration { get; }

    public TimeSpan Duration { get; }
}