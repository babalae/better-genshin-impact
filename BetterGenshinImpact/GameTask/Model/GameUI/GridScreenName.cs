using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public enum GridScreenName
    {
        [Description("武器")]
        Weapons,
        [Description("圣遗物")]
        Artifacts,
        [Description("养成道具")]
        CharacterDevelopmentItems,
        [Description("食物")]
        Food,
        [Description("材料")]
        Materials,
        [Description("小道具")]
        Gadget,
        [Description("任务")]
        Quest,
        [Description("贵重道具")]
        PreciousItems,
        [Description("摆设")]
        Furnishings,
        [Description("圣遗物分解")]
        ArtifactSalvage
    }
}
