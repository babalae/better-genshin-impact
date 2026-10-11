using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatScript(HashSet<string> avatarNames, List<CombatCommand> combatCommands)
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public HashSet<string> AvatarNames { get; set; } = avatarNames;
    public List<CombatCommand> CombatCommands { get; set; } = combatCommands;
}
