namespace BetterGenshinImpact.CombatScript;

public sealed class CommentSymbol : TriviaSymbol
{
    public CommentSymbol(string comment)
    {
        Comment = comment;
    }

    public string Comment { get; }

    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append("//").Append(Comment);
    }
}