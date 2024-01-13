using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.Helpers;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatScriptParser
{
    public static List<CombatCommand> Parse(string script)
    {
        var lines = script.Split(new[] { "\r\n", "\r", "\n", ";" }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        foreach (var line in lines)
        {
            var l = line.Trim()
                .Replace("（", "(")
                .Replace(")", ")")
                .Replace("，", ",");
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
        HashSet<string> combatAvatarNames = new();
        foreach (var line in lines)
        {
            // 以空格分隔角色和指令 截取第一个空格前的内容为角色名称，后面的为指令
            var firstSpaceIndex = line.IndexOf(' ');
            if (firstSpaceIndex < 0)
            {
                Logger.LogError("战斗脚本格式错误，必须以空格分隔角色和指令");
                throw new Exception("战斗脚本格式错误，必须以空格分隔角色和指令");
            }


            var character = line[..firstSpaceIndex];
            character = AvatarAliasToStandardName(character);
            var commands = line[(firstSpaceIndex + 1)..];
            var commandArray = commands.Split(",", StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < commandArray.Length; i++)
            {
                var command = commandArray[i];
                if (string.IsNullOrEmpty(command))
                {
                    continue;
                }

                if (command.Contains("(") && !command.Contains(")"))
                {
                    var j = i + 1;
                    // 括号被逗号分隔，需要合并
                    while (j < commandArray.Length)
                    {
                        command += "," + commandArray[j];
                        if (command.Count("(".Contains) > 1)
                        {
                            Logger.LogError("战斗脚本格式错误，指令 {Cmd} 括号无法配对", command);
                            throw new Exception("战斗脚本格式错误，指令括号无法配对");
                        }

                        if (command.Contains(")"))
                        {
                            i = j;
                            break;
                        }

                        j++;
                    }

                    if (!(command.Contains("(") && command.Contains(")")))
                    {
                        Logger.LogError("战斗脚本格式错误，指令 {Cmd} 括号不完整", command);
                        throw new Exception("战斗脚本格式错误，指令括号不完整");
                    }
                }

                var combatCommand = new CombatCommand(character, command);
                combatCommands.Add(combatCommand);
            }

            combatAvatarNames.Add(character);
        }

        var names = string.Join(",", combatAvatarNames);
        Logger.LogInformation("战斗脚本解析完成，共{Cnt}条指令，涉及角色：{Str}", combatCommands.Count, names);

        return combatCommands;
    }


    public static string AvatarAliasToStandardName(string alias)
    {
        var avatar = DefaultAutoFightConfig.CombatAvatars.Find(x => x.Alias.Contains(alias));
        if (avatar == null)
        {
            throw new Exception($"脚本中的角色名称校验失败：{alias}");
        }

        return avatar.Name;
    }
}