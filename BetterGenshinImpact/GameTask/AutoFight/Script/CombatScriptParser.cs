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
        line = line.Trim();
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

    public static List<int> ParseRoundCommand(CombatCommand roundCommand) {
        // 解析round命令的入参，返回一个整数列表，代表在哪些回合执行后续指令
        // 支持Round(1)、Round(1,3,5)、Round(2-4)、Round(1,3-5)等格式
        var activatingRounds = new List<int>();
        if (roundCommand.Args == null || roundCommand.Args.Count == 0) {
            Logger.LogError("round方法必须有入参，代表在哪些回合执行后续指令，例：round(1)、round(1,3-5)");
            throw new ArgumentException("round方法必须有入参，代表在哪些回合执行后续指令，例：round(1)、round(1,3-5)");
        }
        foreach (var arg in roundCommand.Args) {
            if (arg.Contains('-')) {
                // 范围
                var parts = arg.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2) {
                    Logger.LogError("round方法的入参格式错误，例：round(1-3)");
                    throw new ArgumentException("round方法的入参格式错误，例：round(1-3)");
                }
                var start = int.Parse(parts[0]);
                var end = int.Parse(parts[1]);
                if (start > end || start <= 0) {
                    Logger.LogError("round方法的入参格式错误，起始回合必须小于等于结束回合且大于0，例：round(1-3)");
                    throw new ArgumentException("round方法的入参格式错误，起始回合必须小于等于结束回合且大于0，例：round(1-3)");
                }
                for (int i = start; i <= end; i++) {
                    activatingRounds.Add(i);
                }
            } else {
                // 单个回合
                var round = int.Parse(arg);
                if (round <= 0) {
                    Logger.LogError("round方法的入参格式错误，回合数必须大于0，例：round(1)");
                    throw new ArgumentException("round方法的入参格式错误，回合数必须大于0，例：round(1)");
                }
                activatingRounds.Add(round);
            }
        }
        return activatingRounds;
    }

    public static List<CombatCommand> ParseLineCommands(string lineWithoutAvatar, string avatarName) {
        var parts = lineWithoutAvatar.Split("|", StringSplitOptions.RemoveEmptyEntries);
        var fullCombatCommands = new List<CombatCommand>();
        foreach (var part in parts)
        {
            var combatCommands = ParseLinePart(part, avatarName);
            if (combatCommands.Count > 0 && combatCommands[0].Method == Method.Round) {
                // 遇到round指令，作为回合分隔符使用，不加入最终指令列表
                var roundCommand = combatCommands[0];
                var activatingRounds = ParseRoundCommand(roundCommand);
                combatCommands.RemoveAt(0);
                foreach (var combatCommand in combatCommands) {
                    
                    combatCommand.ActivatingRound = activatingRounds;
                }
            }
            fullCombatCommands.AddRange(combatCommands);
        }
        // foreach (var combatCommand in fullCombatCommands)
        // {
        //     Logger.LogDebug("解析战斗脚本命令：{cmd}", combatCommand.ToString());
        // }
        return fullCombatCommands;
    }

    public static List<CombatCommand> ParseLinePart(string lineWithoutAvatar, string avatarName)
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