using System;

namespace BetterGenshinImpact.CombatScript;

public class LineBreakTriviaSymbol : TriviaSymbol
{
    private readonly string lineBreak;

    public LineBreakTriviaSymbol()
    {
        lineBreak = Environment.NewLine;
    }

    public LineBreakTriviaSymbol(string lineBreak)
    {
        if (lineBreak is not ("\r\n" or "\n" or "\r" or ";"))
        {
            throw new ArgumentException("Invalid line break.");
        }

        this.lineBreak = lineBreak;
    }

    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(lineBreak);
    }
}