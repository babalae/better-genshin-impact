using BetterGenshinImpact.GameTask.AutoFight.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatScriptBag(List<CombatScript> combatScripts)
{
    private List<CombatScript> CombatScripts { get; set; } = combatScripts;

    public CombatScriptBag(CombatScript combatScript) : this([combatScript])
    {
    }

    public List<CombatCommand> FindCombatScript(ReadOnlyCollection<Avatar> avatars)
    {
        var (combatScript, matchCount) = SelectCombatScript(avatars.Select(avatar => avatar.Name).ToArray());
        if (matchCount == avatars.Count)
        {
            Logger.LogInformation("匹配到战斗脚本：{Name}", combatScript.Name);
        }
        else
        {
            Logger.LogWarning("未完整匹配到当前队伍，使用匹配度最高的队伍：{Name}", combatScript.Name);
        }

        return combatScript.CombatCommands;
    }

    internal (CombatScript Script, int MatchCount) SelectCombatScript(IReadOnlyCollection<string> avatarNames)
    {
        CombatScript? bestScript = null;
        var bestMatchCount = 0;

        foreach (var combatScript in CombatScripts)
        {
            var matchCount = 0;
            foreach (var avatarName in avatarNames)
            {
                if (combatScript.AvatarNames.Contains(avatarName))
                {
                    matchCount++;
                }
            }

            if (matchCount == 0)
            {
                continue;
            }

            // 先比较匹配人数；人数相同时，策略角色越少，匹配比例越高。
            // 即使已覆盖全队也继续比较，避免通用策略抢先覆盖专用策略。
            if (bestScript == null
                || matchCount > bestMatchCount
                || (matchCount == bestMatchCount && combatScript.AvatarNames.Count < bestScript.AvatarNames.Count))
            {
                bestScript = combatScript;
                bestMatchCount = matchCount;
            }
            // 两项相同时保留先遇到的策略，不改变候选列表顺序。
        }

        if (bestScript == null)
        {
            throw new Exception("未匹配到任何战斗脚本");
        }

        return (bestScript, bestMatchCount);
    }
}
