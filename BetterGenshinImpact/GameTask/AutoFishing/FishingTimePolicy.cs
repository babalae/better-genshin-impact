﻿using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public enum FishingTimePolicy
    {
        [Description("全天")]
        All,
        [Description("白天")]
        Daytime,
        [Description("夜晚")]
        Nighttime,
        [Description("不调")]
        DontChange
    }
}
