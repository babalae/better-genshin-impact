using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public abstract class InstructionSymbol : BaseSymbol
{
    protected InstructionSymbol(string name, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
    {
        Name = name;
        HasParameterList = false;
        LeadingTriviaList = leadingTriviaList;
        TailingTrivia = tailingTrivia;
    }

    protected InstructionSymbol(string name, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> leadingTriviaList, TriviaSymbol? tailingTrivia)
    {
        Name = name;
        HasParameterList = true;
        ParameterList = parameterList;
        LeadingTriviaList = leadingTriviaList;
        TailingTrivia = tailingTrivia;
    }

    public string Name { get; }

    public bool HasParameterList { get; }

    public ImmutableArray<IParameterSymbol> ParameterList { get; }

    public ImmutableArray<TriviaSymbol> LeadingTriviaList { get; }

    public TriviaSymbol? TailingTrivia { get; set; }

    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(LeadingTriviaList);
        
        if (this is IInstructionSymbolHasAlias {IsAlias: true } hasAlias)
        {
            emitter.Append(hasAlias.AliasName);
        }
        else
        {
            emitter.Append(Name);
        }

        if (HasParameterList)
        {
            emitter.Append('(');
            emitter.Append(ParameterList);
            emitter.Append(')');
        }

        if (TailingTrivia is not null)
        {
            emitter.Append(TailingTrivia);
        }
    }
}