using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.AutoEat
{
    public enum FoodEffectType
    {
        [Description(Lang.S["GameTask_10507_3b56f5"])]
        RecoveryDish,
        [Description(Lang.S["GameTask_10506_064b6b"])]
        ATKBoostingDish,
        [Description(Lang.S["GameTask_10505_00b6b4"])]
        AdventurersDish,
        [Description(Lang.S["GameTask_10504_9ae19b"])]
        DEFBoostingDish,
        [Description(Lang.S["GameTask_10503_722479"])]
        Potion,
        [Description(Lang.S["GameTask_10502_0d98c7"])]
        Other
    }
}
