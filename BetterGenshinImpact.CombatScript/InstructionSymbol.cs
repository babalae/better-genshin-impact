using System.Collections.Immutable;

namespace BetterGenshinImpact.CombatScript;

public abstract class InstructionSymbol : BaseSymbol
{
    protected InstructionSymbol(string name, ImmutableArray<TriviaSymbol> trivia)
    {
        Name = name;
        HasParameterList = false;
        TriviaList = trivia;
    }

    protected InstructionSymbol(string name, ImmutableArray<IParameterSymbol> parameterList, ImmutableArray<TriviaSymbol> trivia)
    {
        Name = name;
        HasParameterList = true;
        ParameterList = parameterList;
        TriviaList = trivia;
    }

    public string Name { get; }

    public bool HasParameterList { get; }

    public ImmutableArray<IParameterSymbol> ParameterList { get; }

    public ImmutableArray<TriviaSymbol> TriviaList { get; }

    public override void Emit(ISymbolEmitter emitter)
    {
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

        emitter.Append(TriviaList);
    }
}