using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoEat
{
    public enum FoodEffectType
    {
        [Description("恢复类料理")]
        RecoveryDish,
        [Description("攻击类料理")]
        ATKBoostingDish,
        [Description("冒险类料理")]
        AdventurersDish,
        [Description("防御类料理")]
        DEFBoostingDish,
        [Description("药剂")]
        Potion,
        [Description("其他")]
        Other
    }
}
