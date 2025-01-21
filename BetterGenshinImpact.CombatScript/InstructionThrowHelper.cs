using System;
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public static class InstructionThrowHelper
{
    public static void ThrowIfParameterListIsDefault(ImmutableArray<IParameterSymbol> parameterList)
    {
        if (parameterList.IsDefault)
        {
            throw new ArgumentException($"Parameter list can not be default.");
        }
    }

    public static void ThrowIfParameterListCountNotCorrect(ImmutableArray<IParameterSymbol> parameterList, FrozenSet<int> allowedCounts)
    {
        if (!allowedCounts.Contains(parameterList.Length))
        {
            throw new ArgumentException($"Instruction parameter count not correct.");
        }
    }

    public static void ThrowIfParameterAtIndexIsNot<T>(ImmutableArray<IParameterSymbol> parameterList, int index, out T symbol)
        where T : IParameterSymbol
    {
        if (parameterList.Length <= index || parameterList[index] is not T result)
        {
            throw new ArgumentException($"Instruction's parameter at index {index} must be a {typeof(T).Name}.");
        }

        symbol = result;
    }
}