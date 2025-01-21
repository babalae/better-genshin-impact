using System.Text;

namespace BetterGenshinImpact.CombatScript;

public sealed class SymbolEmitter : ISymbolEmitter
{
    private readonly StringBuilder builder = new();

    public string Emit()
    {
        return builder.ToString();
    } 

    public ISymbolEmitter Append(char value)
    {
        builder.Append(value);
        return this;
    }
    
    public ISymbolEmitter Append(char value, int repeatCount)
    {
        builder.Append(value, repeatCount);
        return this;
    }

    public ISymbolEmitter Append(double value)
    {
        builder.Append(value);
        return this;
    }

    public ISymbolEmitter Append(string value)
    {
        builder.Append(value);
        return this;
    }
}