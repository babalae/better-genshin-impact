using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public sealed class InstructionListSymbol : BaseSymbol
{
    public InstructionListSymbol(ImmutableArray<InstructionSymbol> instructions)
    {
        Instructions = instructions;
    }

    public ImmutableArray<InstructionSymbol> Instructions { get; }

    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(Instructions);
    }
}