using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BetterGenshinImpact.GameTask.AutoFight.Config;

[Serializable]
public class CombatAvatar
{

    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 角色中文名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色英文名
    /// </summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// 武器类型
    /// </summary>
    public string Weapon { get; set; } = string.Empty;

    /// <summary>
    /// 角色元素。未知角色和无法确定当前元素的旅行者可以不配置。
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public ElementalType? ElementalType { get; set; }

    /// <summary>
    /// 是否支持通过普通攻击进行元素采集。
    /// </summary>
    public bool NormalAttack { get; set; }

    /// <summary>
    /// 是否支持通过元素战技进行元素采集。
    /// </summary>
    public bool ElementalSkill { get; set; }

    /// <summary>
    /// 元素战技CD
    /// </summary>
    public double SkillCd { get; set; }

    /// <summary>
    /// 长按元素战技CD
    /// </summary>
    public double SkillHoldCd { get; set; }

    /// <summary>
    /// 元素爆发CD
    /// </summary>
    public double BurstCd { get; set; }

    /// <summary>
    /// 别名
    /// </summary>
    public List<string> Alias { get; set; } = [];

}
