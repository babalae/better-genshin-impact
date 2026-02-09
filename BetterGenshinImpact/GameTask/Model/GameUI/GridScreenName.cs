using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public enum GridScreenName
    {
        [Description(Lang.S["GameTask_11842_44a3d9"])]
        Weapons,
        [Description(Lang.S["GameTask_11841_5baa97"])]
        Artifacts,
        [Description(Lang.S["GameTask_11840_8622be"])]
        CharacterDevelopmentItems,
        [Description(Lang.S["GameTask_11839_d59546"])]
        Food,
        [Description(Lang.S["GameTask_11838_ebc9d9"])]
        Materials,
        [Description(Lang.S["GameTask_11837_804886"])]
        Gadget,
        [Description(Lang.S["GameTask_11836_0e46d8"])]
        Quest,
        [Description(Lang.S["GameTask_11835_cbf34c"])]
        PreciousItems,
        [Description(Lang.S["GameTask_11834_b1cb76"])]
        Furnishings,
        [Description(Lang.S["Task_ArtifactSalvage"])]
        ArtifactSalvage,
        [Description(Lang.S["GameTask_11833_68426d"])]
        ArtifactSetFilter
    }
}
