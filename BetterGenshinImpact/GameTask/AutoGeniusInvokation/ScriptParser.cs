using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Config;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class ScriptParser
{
    private static readonly ILogger<ScriptParser> MyLogger = App.GetLogger<ScriptParser>();

    public static Duel Parse(string script)
    {
        var lines = script.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var result = new List<string>();
        foreach (var line in lines)
        {
            string l = line.Trim();
            result.Add(l);
        }

        return Parse(result);
    }

    public static Duel Parse(List<string> lines)
    {
        Duel duel = new();
        string stage = "";

        int i = 0;
        try
        {
            for (i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Contains(':'))
                {
                    stage = line;
                    continue;
                }

                if (line == "---" || line.StartsWith("//") || string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (stage == Lang.S["GameTask_10910_17b3fb"])
                {
                    var character = ParseCharacter(line);
                    duel.Characters[character.Index] = character;
                }
                else if (stage == Lang.S["GameTask_10909_12e6f0"])
                {
                    MyAssert(duel.Characters[3] != null, Lang.S["GameTask_10908_f268a5"]);

                    string[] actionParts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    MyAssert(actionParts.Length == 3, Lang.S["GameTask_10907_4cf47d"]);
                    MyAssert(actionParts[1] == Lang.S["GameTask_10392_ecff77"], "策略中的行动命令解析错误");

                    var actionCommand = new ActionCommand();
                    var action = actionParts[1].ChineseToActionEnum();
                    actionCommand.Action = action;

                    int j = 1;
                    for (j = 1; j <= 3; j++)
                    {
                        var character = duel.Characters[j];
                        if (character != null && character.Name == actionParts[0])
                        {
                            actionCommand.Character = character;
                            break;
                        }
                    }

                    MyAssert(j <= 3, Lang.S["GameTask_10906_6ada9b"]);

                    int skillNum = int.Parse(RegexHelper.ExcludeNumberRegex().Replace(actionParts[2], ""));
                    MyAssert(skillNum < 5, Lang.S["GameTask_10905_19735d"]);
                    actionCommand.TargetIndex = skillNum;
                    duel.ActionCommandQueue.Add(actionCommand);
                }
                else
                {
                    throw new System.Exception($"{Lang.S["GameTask_10904_007d5b"]});
                }
            }

            MyAssert(duel.Characters[3] != null, Lang.S["GameTask_10903_fa74e3"]);
        }
        catch (System.Exception ex)
        {
            MyLogger.LogError($"{Lang.S["GameTask_10901_ba69de"]});
            ThemedMessageBox.Error($"{Lang.S["GameTask_10901_ba69de"]}, "策略解析失败");
            return default!;
        }

        return duel;
    }

    /// <summary>
    /// 解析示例
    /// 角色1=刻晴|雷{技能3消耗=1雷骰子+2任意,技能2消耗=3雷骰子,技能1消耗=4雷骰子}
    /// 角色2=雷神|雷{技能3消耗=1雷骰子+2任意,技能2消耗=3雷骰子,技能1消耗=4雷骰子}
    /// 角色3=甘雨|冰{技能4消耗=1冰骰子+2任意,技能3消耗=1冰骰子,技能2消耗=5冰骰子,技能1消耗=3冰骰子}
    /// </summary>
    /// <param name="line"></param>
    /// <returns></returns>
    public static Character ParseCharacter(string line)
    {
        var character = new Character();

        var characterAndSkill = line.Split('{');

        var parts = characterAndSkill[0].Split('=');
        character.Index = int.Parse(RegexHelper.ExcludeNumberRegex().Replace(parts[0].Trim(), ""));
        MyAssert(character.Index >= 1 && character.Index <= 3, Lang.S["GameTask_10900_99c3f1"]);

        if (parts[1].Contains('|'))
        {
            var nameAndElement = parts[1].Split('|');
            character.Name = nameAndElement[0];
            character.Element = nameAndElement[1][..1].ChineseToElementalType();

            // 技能
            string skillStr = characterAndSkill[1].Replace("}", "");
            var skillParts = skillStr.Split(',');
            var skills = new Skill[skillParts.Length + 1];
            for (int i = 0; i < skillParts.Length; i++)
            {
                var skill = ParseSkill(skillParts[i]);
                skills[skill.Index] = skill;
            }

            character.Skills = [.. skills];
        }
        else
        {
            // 没有配置直接使用默认配置
            character.Name = parts[1];
            var standardName = DefaultTcgConfig.CharacterCardMap.Keys.FirstOrDefault(x => x.Equals(character.Name));
            if (string.IsNullOrEmpty(standardName))
            {
                standardName = DefaultAutoFightConfig.AvatarAliasToStandardName(character.Name);
            }

            if (DefaultTcgConfig.CharacterCardMap.TryGetValue(standardName, out var characterCard))
            {
                CharacterCard.CopyCardProperty(character, characterCard);
            }
            else
            {
                throw new System.Exception($"{Lang.S["GameTask_10899_789c84"]});
            }
        }

        return character;
    }

    /// <summary>
    /// 技能3消耗=1雷骰子+2任意
    /// 技能2消耗=3雷骰子
    /// 技能1消耗=4雷骰子
    /// </summary>
    /// <param name="oneSkillStr"></param>
    /// <returns></returns>
    public static Skill ParseSkill(string oneSkillStr)
    {
        var skill = new Skill();
        var parts = oneSkillStr.Split('=');
        skill.Index = short.Parse(RegexHelper.ExcludeNumberRegex().Replace(parts[0], ""));
        MyAssert(skill.Index >= 1 && skill.Index <= 5, Lang.S["GameTask_10898_2f6c84"]);
        var costStr = parts[1];
        var costParts = costStr.Split('+');
        skill.SpecificElementCost = int.Parse(costParts[0][..1]);
        skill.Type = costParts[0].Substring(1, 1).ChineseToElementalType();
        // 杂色骰子在+号右边
        if (costParts.Length > 1)
        {
            skill.AnyElementCost = int.Parse(costParts[1][..1]);
        }

        skill.AllCost = skill.SpecificElementCost + skill.AnyElementCost;
        return skill;
    }

    private static void MyAssert(bool b, string msg)
    {
        if (!b)
        {
            throw new System.Exception(msg);
        }
    }
}
