using BetterGenshinImpact.Helpers;
﻿using System;
using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

public enum DisplayMapTypes
{
    [Description(Lang.S["GameTask_11673_32269b"])]
    Teyvat,

    [Description(Lang.S["GameTask_11672_94e546"])]
    TheChasm,

    [Description(Lang.S["GameTask_11397_9e13be"])]
    Enkanomiya,

    [Description(Lang.S["GameTask_11671_9778f1"])]
    SeaOfBygoneEras,

    [Description(Lang.S["GameTask_11670_c37935"])]
    AncientSacredMountain,
}