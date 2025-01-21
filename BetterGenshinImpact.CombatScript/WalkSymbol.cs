using System;
using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public class WalkSymbol : InstructionSymbol, IInstructionSymbolHasAlias, IInstructionSymbolHasDuration, IParameterSymbol
{
    public WalkSymbol(WalkDirection direction, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("walk", parameterList, leadingTriviaList, tailingTrivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [1]);

        InstructionThrowHelper.ThrowIfParameterAtIndexIsNot(parameterList, 0, out DoubleSymbol doubleSymbol);

        HasDuration = true;
        Duration = TimeSpan.FromSeconds(doubleSymbol.Value);

        Direction = direction;

        IsAlias = true;
    }

    // Used for parameter
    public WalkSymbol(WalkDirection direction, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("walk", leadingTriviaList, tailingTrivia)
    {
        Direction = direction;
        IsAlias = true;
    }

    public WalkSymbol(ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
        : base("walk", parameterList, leadingTriviaList, tailingTrivia)
    {
        InstructionThrowHelper.ThrowIfParameterListIsDefault(parameterList);
        InstructionThrowHelper.ThrowIfParameterListCountNotCorrect(parameterList, [2]);

        InstructionThrowHelper.ThrowIfParameterAtIndexIsNot(parameterList, 0, out WalkSymbol directionAlias);
        InstructionThrowHelper.ThrowIfParameterAtIndexIsNot(parameterList, 1, out DoubleSymbol doubleSymbol);

        Direction = directionAlias.Direction;
        HasDuration = true;
        Duration = TimeSpan.FromSeconds(doubleSymbol.Value);

        IsAlias = false;
    }

    public string AliasName
    {
        get
        {
            return Direction switch
            {
                WalkDirection.Forward => "w",
                WalkDirection.Backward => "s",
                WalkDirection.Left => "a",
                WalkDirection.Right => "d",
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }

    public bool IsAlias { get; }

    public WalkDirection Direction { get; }

    public bool HasDuration { get; }

    public TimeSpan Duration { get; }
}