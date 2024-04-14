using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatScript(HashSet<string> avatarNames, List<CombatCommand> combatCommands)
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public HashSet<string> AvatarNames { get; set; } = avatarNames;
    public List<CombatCommand> CombatCommands { get; set; } = combatCommands;

    /// <summary>
    /// 用于记录和队伍角色匹配到的数量
    /// </summary>
    public int MatchCount { get; set; } = 0;
}
