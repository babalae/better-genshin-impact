using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 队伍内的角色
/// </summary>
public class Character
{
    /// <summary>
    /// 角色名称 中文
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 角色名称 英文
    /// </summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// 队伍内序号
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 元素战技CD
    /// </summary>
    public int SkillCooldown { get; set; }

    /// <summary>
    /// 元素战技是否就绪
    /// </summary>
    public bool IsSkillReady { get; set; }

    /// <summary>
    /// 元素爆发CD
    /// </summary>
    public int BurstCooldown { get; set; }

    /// <summary>
    /// 元素爆发是否就绪
    /// </summary>
    public bool IsBurstReady { get; set; }

    /// <summary>
    /// 右侧角色所在矩形区域
    /// </summary>
    public Rect Rect { get; set; }

    public Character(string name, int index)
    {
        Name = name;
        Index = index;
    }
}