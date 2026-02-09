using System.Collections.Frozen;
using System.Collections.Generic;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    /// <summary>
    /// 圣遗物词条
    /// </summary>
    public class ArtifactAffix(ArtifactAffixType type, float value, bool isUnactivated)
    {
        public ArtifactAffix(ArtifactAffixType type, float value) : this(type, value, false)
        {
        }

        public ArtifactAffixType Type { get; private set; } = type;
        public float Value { get; private set; } = value;
        public bool IsUnactivated { get; private set; } = isUnactivated;

        public static FrozenDictionary<ArtifactAffixType, string> DefaultStrDic { get; } = new Dictionary<ArtifactAffixType, string>() {
            { ArtifactAffixType.ATK, Lang.S["GameTask_10342_067f52"] },
            { ArtifactAffixType.ATKPercent, Lang.S["GameTask_10342_067f52"] },
            { ArtifactAffixType.DEF, Lang.S["GameTask_10341_0aba42"] },
            { ArtifactAffixType.DEFPercent, Lang.S["GameTask_10341_0aba42"] },
            { ArtifactAffixType.HP, Lang.S["GameTask_10340_37be5a"] },
            { ArtifactAffixType.HPPercent, Lang.S["GameTask_10340_37be5a"] },
            { ArtifactAffixType.CRITRate, Lang.S["GameTask_10339_1b2a12"] },
            { ArtifactAffixType.CRITDMG, Lang.S["GameTask_10338_32eaa2"] },
            { ArtifactAffixType.ElementalMastery, Lang.S["GameTask_10337_06402c"] },
            { ArtifactAffixType.EnergyRecharge, Lang.S["GameTask_10336_62f0d2"] },
            { ArtifactAffixType.HealingBonus, Lang.S["GameTask_10335_54e972"] },
            { ArtifactAffixType.PhysicalDMGBonus, Lang.S["GameTask_10334_f6d170"] },
            { ArtifactAffixType.PyroDMGBonus, Lang.S["GameTask_10333_bbc335"] },
            { ArtifactAffixType.HydroDMGBonus, Lang.S["GameTask_10332_9d5cec"] },
            { ArtifactAffixType.DendroDMGBonus, Lang.S["GameTask_10331_9c9d9d"] },
            { ArtifactAffixType.ElectroDMGBonus, Lang.S["GameTask_10330_e3f1b2"] },
            { ArtifactAffixType.AnemoDMGBonus, Lang.S["GameTask_10329_54650c"] },
            { ArtifactAffixType.CryoDMGBonus, Lang.S["GameTask_10328_59d6d1"] },
            { ArtifactAffixType.GeoDMGBonus, Lang.S["GameTask_10327_b467ee"] }
        }.ToFrozenDictionary();
    }

    public enum ArtifactAffixType
    {
        ATK,
        ATKPercent,
        DEF,
        DEFPercent,
        HP,
        HPPercent,
        CRITRate,
        CRITDMG,
        ElementalMastery,
        EnergyRecharge,
        HealingBonus,
        PhysicalDMGBonus,
        /// <summary>
        /// 火
        /// </summary>
        PyroDMGBonus,
        /// <summary>
        /// 水
        /// </summary>
        HydroDMGBonus,
        /// <summary>
        /// 草
        /// </summary>
        DendroDMGBonus,
        /// <summary>
        /// 雷
        /// </summary>
        ElectroDMGBonus,
        /// <summary>
        /// 风
        /// </summary>
        AnemoDMGBonus,
        /// <summary>
        /// 冰
        /// </summary>
        CryoDMGBonus,
        /// <summary>
        /// 岩
        /// </summary>
        GeoDMGBonus,
    }
}
