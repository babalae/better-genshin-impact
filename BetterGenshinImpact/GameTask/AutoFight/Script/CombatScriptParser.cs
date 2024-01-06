using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.Helpers;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatScriptParser
{
    public static List<CombatCommand> Parse(string script)
    {
        var lines = script.Split(new[] { "\r\n", "\r", "\n", ";" }, StringSplitOptions.None);
        var result = new List<string>();
        foreach (var line in lines)
        {
            var l = line.Trim();
            if (l.StartsWith("//") || l.StartsWith("#") || string.IsNullOrEmpty(l))
            {
                continue;
            }

            result.Add(l);
        }

        return Parse(result);
    }

    public static List<CombatCommand> Parse(List<string> lines)
    {
        List<CombatCommand> combatCommands = new();
        foreach (var line in lines)
        {
            var charWithCommands = line.Split(" ");
            var character = charWithCommands[0];
            AssertUtils.IsTrue(DefaultAutoFightConfig.CombatAvatarNames.Contains(character), "角色名称不存在：" + character);
            var commands = charWithCommands[1];
            var commandArray = commands.Split(",");
            foreach (var command in commandArray)
            {
                var combatCommand = new CombatCommand(character, command);
                combatCommands.Add(combatCommand);
            }
        }

        return combatCommands;
    }
}