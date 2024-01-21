using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Config;

[Serializable]
public class CostItem
{
    /// <summary>
    /// 唯一id
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 类型名
    /// </summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// unaligned_element	无色元素
    /// energy	充能
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 消耗多少
    /// </summary>
    public int Count { get; set; }
}

[Serializable]
public class SkillsItem
{
    /// <summary>
    /// 
    /// </summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// 流天射术
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    public List<string> SkillTag { get; set; } = new();

    /// <summary>
    /// 
    /// </summary>
    public List<CostItem> Cost { get; set; } = new();
}

[Serializable]
public class CharacterCard
{
    /// <summary>
    /// 唯一id
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 甘雨
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    public int Hp { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public int Energy { get; set; }

    /// <summary>
    /// 冰元素
    /// </summary>
    public string Element { get; set; } = string.Empty;

    /// <summary>
    /// 弓
    /// </summary>
    public string Weapon { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    public List<SkillsItem> Skills { get; set; } = new();

    public static void CopyCardProperty(Character source, CharacterCard characterCard)
    {
        try
        {
            source.Element = characterCard.Element.Replace("元素", "").ChineseToElementalType();
            source.Hp = characterCard.Hp;
            source.Skills = new Skill[characterCard.Skills.Count + 1];

            short skillIndex = 0;
            for (var i = characterCard.Skills.Count - 1; i >= 0; i--)
            {
                var skillsItem = characterCard.Skills[i];
                if (skillsItem.SkillTag.Contains("被动技能"))
                {
                    continue;
                }

                skillIndex++;

                source.Skills[skillIndex] = GetSkill(skillsItem);
                source.Skills[skillIndex].Index = skillIndex;
            }
        }
        catch (System.Exception e)
        {
            TaskControl.Logger.LogError($"角色【{characterCard.Name}】卡牌配置解析失败：{e.Message}");
            throw new System.Exception($"角色【{characterCard.Name}】卡牌配置解析失败：{e.Message}。请自行进行角色定义", e);
        }
    }

    public static Skill GetSkill(SkillsItem skillsItem)
    {
        Skill skill = new();
        var specificElementNum = 0;
        foreach (var cost in skillsItem.Cost)
        {
            if (cost.NameEn == "unaligned_element")
            {
                skill.AnyElementCost = cost.Count;
            }
            else if (cost.NameEn == "energy")
            {
                continue;
            }
            else
            {
                skill.SpecificElementCost = cost.Count;
                skill.Type = cost.NameEn.ToElementalType();
                specificElementNum++;
            }
        }

        if (specificElementNum != 1)
        {
            throw new System.Exception($"技能[{skillsItem.Name}]默认技能数据技能解析失败");
        }

        skill.AllCost = skill.SpecificElementCost + skill.AnyElementCost;
        return skill;
    }
}