using BetterGenshinImpact.Helpers;
﻿using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public enum FishingTimePolicy
    {
        [Description(Lang.S["GameTask_10781_bd4357"])]
        All,
        [Description(Lang.S["GameTask_10780_4ed52b"])]
        Daytime,
        [Description(Lang.S["GameTask_10779_86de2b"])]
        Nighttime,
        [Description(Lang.S["GameTask_10778_bfa7fc"])]
        DontChange
    }
}
