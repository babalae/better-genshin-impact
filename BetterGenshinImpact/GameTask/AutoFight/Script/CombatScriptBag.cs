using BetterGenshinImpact.GameTask.AutoFight.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        foreach (var combatScript in CombatScripts)
        {
            var matchCount = 0;
            foreach (var avatar in avatars)
            {
                if (combatScript.AvatarNames.Contains(avatar.Name))
                {
                    matchCount++;
                }

                if (matchCount != avatars.Count) continue;
                Logger.LogInformation("匹配到战斗脚本：{Name}，共{Cnt}条指令，涉及角色：{Str}", 
                combatScript.Name, combatScript.CombatCommands.Count, string.Join(",", combatScript.AvatarNames));  
                return combatScript.CombatCommands;
            }

            combatScript.MatchCount = matchCount;
        }

        // 没有找到匹配的战斗脚本
        // 按照匹配数量降序排序
        CombatScripts.Sort((a, b) => b.MatchCount.CompareTo(a.MatchCount));
        if (CombatScripts[0].MatchCount == 0)
        {
            throw new Exception("未匹配到任何战斗脚本");
        }

        Logger.LogWarning("未完整匹配到四人队伍，使用匹配度最高的队伍：{Name}", CombatScripts[0].Name);
        return CombatScripts[0].CombatCommands;
    }
}
