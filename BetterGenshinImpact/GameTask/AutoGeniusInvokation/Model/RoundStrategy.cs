using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;

[Obsolete]
public class RoundStrategy
{
    public List<string> RawCommandList { get; set; } = new List<string>();

    public List<ActionCommand> ActionCommands { get; set; } = new List<ActionCommand>();

    public List<ElementalType> MaybeNeedElement(Duel duel)
    {
        List<ElementalType> result = new List<ElementalType>();

        for (int i = 0; i < ActionCommands.Count; i++)
        {
            if (ActionCommands[i].Action == ActionEnum.SwitchLater 
                && i != ActionCommands.Count-1
                && ActionCommands[i+1].Action == ActionEnum.UseSkill)
            {
                result.Add(duel.Characters[ActionCommands[i].TargetIndex].Element);
            }
        }
        return result;
    }
}