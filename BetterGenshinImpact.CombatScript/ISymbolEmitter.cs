namespace BetterGenshinImpact.CombatScript;

public interface ISymbolEmitter
{
    string Emit();

    ISymbolEmitter Append(char value);

    ISymbolEmitter Append(char value, int repeatCount);
    
    ISymbolEmitter Append(double value);

    ISymbolEmitter Append(string value);
}