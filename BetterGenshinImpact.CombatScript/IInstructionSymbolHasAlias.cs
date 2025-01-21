namespace BetterGenshinImpact.CombatScript;

public interface IInstructionSymbolHasAlias
{
    public string AliasName { get; }

    public bool IsAlias { get; }
}