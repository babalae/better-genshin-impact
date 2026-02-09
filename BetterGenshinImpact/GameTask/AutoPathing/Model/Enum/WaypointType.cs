using BetterGenshinImpact.Helpers;
﻿using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class WaypointType(string code, string msg)
{
    public static readonly WaypointType Path = new("path", Lang.S["GameTask_11184_fda9cc"]);
    public static readonly WaypointType Target = new("target", Lang.S["GameTask_11183_ded563"]);
    public static readonly WaypointType Teleport = new("teleport", Lang.S["GameTask_11182_e3736f"]);
    public static readonly WaypointType Orientation = new("orientation", Lang.S["GameTask_11181_3e7eae"]);

    public static IEnumerable<WaypointType> Values
    {
        get
        {
            yield return Path;
            yield return Target;
            yield return Teleport;
            yield return Orientation;
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
