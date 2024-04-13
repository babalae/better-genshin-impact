using System;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using System.Collections.Generic;
using System.Windows.Documents;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatScriptBag(List<CombatScript> combatScripts)
{
    private List<CombatScript> CombatScripts { get; set; } = combatScripts;

    public CombatScriptBag(CombatScript combatScript) : this([combatScript])
    {
    }

    public List<CombatCommand> FindCombatScript(Avatar[] avatars)
    {
        foreach (var combatScript in CombatScripts)
        {
            var matchCount = 0;
            foreach (var avatar in avatars)
            {
                if (combatScript.AvatarNames.Contains(avatar.Name))
                {
                    matchCount++;
                }

                if (matchCount == avatars.Length)
                {
                    return combatScript.CombatCommands;
                }
            }
        }

        throw new Exception("未找到匹配的战斗脚本");
    }
}
