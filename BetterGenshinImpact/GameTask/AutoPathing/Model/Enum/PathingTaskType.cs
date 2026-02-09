using BetterGenshinImpact.Helpers;
﻿using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class PathingTaskType(string code, string msg)
{
    public static readonly PathingTaskType Collect = new("collect", Lang.S["GameTask_11180_e8bc26"]);
    public static readonly PathingTaskType Mining = new("mining", Lang.S["GameTask_11162_09401a"]);
    public static readonly PathingTaskType Farming = new("farming", Lang.S["GameTask_11179_e9c565"]);

    public static IEnumerable<PathingTaskType> Values
    {
        get
        {
            yield return Collect;
            yield return Mining;
            yield return Farming;
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
