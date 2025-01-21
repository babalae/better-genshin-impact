using System;

namespace BetterGenshinImpact.CombatScript;

public interface IInstructionSymbolHasDuration
{
    public bool HasDuration { get; }

    public TimeSpan Duration { get; }
}