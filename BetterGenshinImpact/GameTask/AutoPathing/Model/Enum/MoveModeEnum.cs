using BetterGenshinImpact.Helpers;
﻿using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class MoveModeEnum(string code, string msg)
{
    public static readonly MoveModeEnum Walk = new("walk", Lang.S["GameTask_11178_0da405"]);
    public static readonly MoveModeEnum Run = new("run", Lang.S["GameTask_11177_b82ad5"]);
    public static readonly MoveModeEnum Dash = new("dash", Lang.S["GameTask_11176_531771"]);
    public static readonly MoveModeEnum Climb = new("climb", Lang.S["GameTask_11175_717cee"]);
    public static readonly MoveModeEnum Fly = new("fly", Lang.S["GameTask_11174_8868c2"]);
    public static readonly MoveModeEnum Jump = new("jump", Lang.S["GameTask_10668_fe8959"]);
    public static readonly MoveModeEnum Swim = new("swim", Lang.S["GameTask_11173_221486"]);

    public static IEnumerable<MoveModeEnum> Values
    {
        get
        {
            yield return Walk;
            yield return Fly;
            yield return Jump;
            yield return Swim;
        }
    }

    public string Code { get; private set; } = code;
    public string Msg { get; private set; } = msg;

    public static string GetMsgByCode(string code)
    {
        foreach (var item in Values)
        {
            if (item.Code == code)
            {
                return item.Msg;
            }
        }
        return code;
    }
}
