using BetterGenshinImpact.Helpers;
﻿namespace BetterGenshinImpact.Helpers.Extensions;

public static class BooleanExtension
{
    public static string ToChinese(this bool enabled)
    {
        return enabled ? Lang.S["Gen_11917_cc42dd"] : "关闭";
    }
}