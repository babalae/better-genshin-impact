using BetterGenshinImpact.Helpers;
﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class Method
{
    public static readonly Method Skill = new(["skill", "e"]);
    public static readonly Method Burst = new(["burst", "q"]);
    public static readonly Method Attack = new(["attack", Lang.S["GameTask_10675_0fdf02"], "普通攻击"]);
    public static readonly Method Charge = new(["charge", Lang.S["GameTask_10674_7f3b6c"]]);
    public static readonly Method Wait = new(["wait", "after", Lang.S["GameTask_10673_879792"]]);
    public static readonly Method Ready = new(["ready", Lang.S["GameTask_10672_769d88"]]);

    public static readonly Method Walk = new(["walk", Lang.S["GameTask_10671_22a207"]]);
    public static readonly Method W = new(["w"]);
    public static readonly Method A = new(["a"]);
    public static readonly Method S = new(["s"]);
    public static readonly Method D = new(["d"]);

    public static readonly Method Aim = new(["aim", "r", Lang.S["GameTask_10670_039c37"]]);
    public static readonly Method Dash = new(["dash", Lang.S["GameTask_10669_fc16d9"]]);
    public static readonly Method Jump = new(["jump", "j", Lang.S["GameTask_10668_fe8959"]]);

    // 宏
    public static readonly Method MouseDown = new(["mousedown"]);
    public static readonly Method MouseUp = new(["mouseup"]);
    public static readonly Method Click = new(["click"]);
    public static readonly Method MoveBy = new(["moveby"]);
    public static readonly Method KeyDown = new(["keydown"]);
    public static readonly Method KeyUp = new(["keyup"]);
    public static readonly Method KeyPress = new(["keypress"]);
    public static readonly Method Scroll = new(["scroll", "verticalscroll"]);
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
            yield return Ready;

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
            yield return Scroll;
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

        Logger.LogError($"{Lang.S["GameTask_10667_bcb659"]});
        throw new ArgumentException($"{Lang.S["GameTask_10667_bcb659"]});
    }
}