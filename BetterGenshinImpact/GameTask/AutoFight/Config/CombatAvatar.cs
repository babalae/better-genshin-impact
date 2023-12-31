using System;

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

}