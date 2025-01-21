using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public sealed class AvatarInstructionListSymbol : BaseSymbol
{
    public AvatarInstructionListSymbol(AvatarSymbol avatar, ImmutableArray<TriviaSymbol> triviaList, InstructionListSymbol instructionList)
    {
        Avatar = avatar;
        TriviaList = triviaList;
        InstructionList = instructionList;
    }

    public AvatarSymbol Avatar { get; }

    public ImmutableArray<TriviaSymbol> TriviaList { get; }

    public InstructionListSymbol InstructionList { get; }

    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(Avatar).Append(TriviaList).Append(InstructionList);
    }
}