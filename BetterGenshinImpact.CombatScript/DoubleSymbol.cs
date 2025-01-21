namespace BetterGenshinImpact.CombatScript;

public sealed class DoubleSymbol : BaseSymbol, IParameterSymbol
{
    public DoubleSymbol(double value)
    {
        Value = value;
    }

    public double Value { get; }

    public override void Emit(ISymbolEmitter emitter)
    {
        emitter.Append(Value);
    }
}