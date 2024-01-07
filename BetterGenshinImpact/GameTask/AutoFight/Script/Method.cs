using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class Method
{
    public static readonly Method Skill = new(new() { "skill", "e" });
    public static readonly Method Burst = new(new() { "burst", "q" });
    public static readonly Method Attack = new(new() { "attack", "普攻", "普通攻击" });
    public static readonly Method Charge = new(new() { "charge", "重击" });
    public static readonly Method Wait = new(new() { "wait", "after", "等待" });

    public static readonly Method Walk = new(new() { "walk", "行走" });
    public static readonly Method W = new(new() { "w" });
    public static readonly Method A = new(new() { "a" });
    public static readonly Method S = new(new() { "s" });
    public static readonly Method D = new(new() { "d" });

    public static readonly Method Aim = new(new() { "aim", "r", "瞄准" });
    public static readonly Method Dash = new(new() { "dash", "冲刺" });
    public static readonly Method Jump = new(new() { "jump", "j", "跳跃" });

    public static IEnumerable<Method> Values
    {
        get
        {
            yield return Skill;
            yield return Burst;
            yield return Attack;
            yield return Charge;
            yield return Wait;

            yield return Walk;
            yield return W;
            yield return A;
            yield return S;

            yield return Aim;
            yield return Dash;
            yield return Jump;
        }
    }

    /// <summary>
    /// 别名
    /// </summary>
    public List<string> Alias { get; private set; }

    public Method(List<string> alias)
    {
        Alias = alias;
    }

    public static Method GetEnumByCode(string method)
    {
        foreach (var m in Values)
        {
            if (m.Alias.Contains(method))
            {
                return m;
            }
        }

        throw new ArgumentException($"未知的方法：{method}");
    }
}