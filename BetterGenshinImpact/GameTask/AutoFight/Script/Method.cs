using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class Method
{
    public static readonly Method Skill = new(["skill", "e"]);
    public static readonly Method Burst = new(["burst", "q"]);
    public static readonly Method Attack = new(["attack", "普攻", "普通攻击"]);
    public static readonly Method Charge = new(["charge", "重击"]);
    public static readonly Method Wait = new(["wait", "after", "等待"]);

    public static readonly Method Walk = new(["walk", "行走"]);
    public static readonly Method W = new(["w"]);
    public static readonly Method A = new(["a"]);
    public static readonly Method S = new(["s"]);
    public static readonly Method D = new(["d"]);

    public static readonly Method Aim = new(["aim", "r", "瞄准"]);
    public static readonly Method Dash = new(["dash", "冲刺"]);
    public static readonly Method Jump = new(["jump", "j", "跳跃"]);

    // 宏
    public static readonly Method MouseDown = new(["mousedown"]);
    public static readonly Method MouseUp = new(["mouseup"]);
    public static readonly Method Click = new(["click"]);
    public static readonly Method MoveBy = new(["moveby"]);
    public static readonly Method KeyDown = new(["keydown"]);
    public static readonly Method KeyUp = new(["keyup"]);
    public static readonly Method KeyPress = new(["keypress"]);
    public static readonly Method Round = new(["round"]);

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
            yield return D;

            // yield return Aim;
            yield return Dash;
            yield return Jump;

            // 宏
            yield return MouseDown;
            yield return MouseUp;
            yield return Click;
            yield return MoveBy;
            yield return KeyDown;
            yield return KeyUp;
            yield return KeyPress;
            yield return Round;
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

        Logger.LogError($"战斗策略脚本中出现未知的方法：{method}");
        throw new ArgumentException($"战斗策略脚本中出现未知的方法：{method}");
    }
}