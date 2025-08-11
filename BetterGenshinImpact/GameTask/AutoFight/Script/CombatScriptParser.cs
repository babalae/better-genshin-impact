using BetterGenshinImpact.GameTask.AutoFight.Config;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatScriptParser
{
    public static string CurrentAvatarName = "当前角色";
    
    public static CombatScriptBag ReadAndParse(string path)
    {
        if (File.Exists(path))
        {
            return new CombatScriptBag(Parse(path));
        }
        else if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                Logger.LogError("战斗脚本文件不存在：{Path}", path);
                throw new Exception("战斗脚本文件不存在");
            }

            var combatScripts = new List<CombatScript>();
            foreach (var file in files)
            {
                try
                {
                    combatScripts.Add(Parse(file));
                }
                catch (Exception e)
                {
                    Logger.LogWarning("解析战斗脚本文件失败：{Path} , {Msg} ", file, e.Message);
                }
            }

            return new CombatScriptBag(combatScripts);
        }
        else
        {
            Logger.LogError("战斗脚本文件不存在：{Path}", path);
            throw new Exception("战斗脚本文件不存在");
        }
    }

    public static CombatScript Parse(string path)
    {
        var script = File.ReadAllText(path);
        var combatScript = ParseContext(script);
        combatScript.Path = path;
        combatScript.Name = Path.GetFileNameWithoutExtension(path);
        return combatScript;
    }

    public static CombatScript ParseContext(string context, bool validate = true)
    {
        var lines = context.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        foreach (var line in lines)
        {
            var l = line.Trim()
                .Replace("（", "(")
                .Replace(")", ")")
                .Replace("，", ",");
            if (l.StartsWith("//") || l.StartsWith('#') || string.IsNullOrEmpty(l))
            {
                continue;
            }

            if (l.Contains(";"))
            {
                result.AddRange(l.Split(";", StringSplitOptions.RemoveEmptyEntries));
            }
            else
            {
                result.Add(l);
            }
        }

        return ParseLines(result, validate);
    }

    private static CombatScript ParseLines(List<string> lines, bool validate = true)
    {
        List<CombatCommand> combatCommands = [];
        HashSet<string> combatAvatarNames = [];
        foreach (var line in lines)
        {
            var oneLineCombatCommands = ParseLine(line, combatAvatarNames, validate);
            combatCommands.AddRange(oneLineCombatCommands);
        }

        var names = string.Join(",", combatAvatarNames);
        Logger.LogDebug("战斗脚本解析完成，共{Cnt}条指令，涉及角色：{Str}", combatCommands.Count, names);

        return new CombatScript(combatAvatarNames, combatCommands);
    }

    private static List<CombatCommand> ParseLine(string line, HashSet<string> combatAvatarNames, bool validate = true)
    {
        var oneLineCombatCommands = new List<CombatCommand>();
        // 以空格分隔角色和指令 截取第一个空格前的内容为角色名称，后面的为指令
        // 20241116更新 不输入角色名称时，直接以当前角色为准
        var firstSpaceIndex = line.IndexOf(' ');
        var character = CurrentAvatarName;
        var commands = line;
        if (firstSpaceIndex > 0)
        {
            character = line[..firstSpaceIndex];
            character = DefaultAutoFightConfig.AvatarAliasToStandardName(character);
            commands = line[(firstSpaceIndex + 1)..];
        }
        else
        {
            if (validate)
            {
                Logger.LogError("战斗脚本格式错误，必须以空格分隔角色和指令");
                throw new Exception("战斗脚本格式错误，必须以空格分隔角色和指令");
            }
        }

        oneLineCombatCommands.AddRange(ParseLineCommands(commands, character));
        combatAvatarNames.Add(character);
        return oneLineCombatCommands;
    }

    public static List<CombatCommand> ParseLineCommands(string lineWithoutAvatar, string avatarName)
    {
        var oneLineCombatCommands = new List<CombatCommand>();
        var commandArray = lineWithoutAvatar.Split(",", StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < commandArray.Length; i++)
        {
            var command = commandArray[i];
            if (string.IsNullOrEmpty(command))
            {
                continue;
            }

            if (command.Contains('(') && !command.Contains(')'))
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

                    if (command.Contains(')'))
                    {
                        i = j;
                        break;
                    }

                    j++;
                }

                if (!(command.Contains('(') && command.Contains(')')))
                {
                    Logger.LogError("战斗脚本格式错误，指令 {Cmd} 括号不完整", command);
                    throw new Exception("战斗脚本格式错误，指令括号不完整");
                }
            }

            var combatCommand = new CombatCommand(avatarName, command);
            oneLineCombatCommands.Add(combatCommand);
        }

        return oneLineCombatCommands;
    }
}