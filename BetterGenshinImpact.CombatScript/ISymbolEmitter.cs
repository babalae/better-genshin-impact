namespace BetterGenshinImpact.CombatScript;

public interface ISymbolEmitter
{
    string Emit();

    ISymbolEmitter Append(char value);

    ISymbolEmitter Append(double value);

    ISymbolEmitter Append(string value);
}