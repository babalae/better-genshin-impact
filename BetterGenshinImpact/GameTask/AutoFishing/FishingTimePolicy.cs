using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public enum FishingTimePolicy
    {
        [Description("全天")]
        All,
        [Description("白天")]
        Daytime,
        [Description("夜晚")]
        Nighttime
    }
}
