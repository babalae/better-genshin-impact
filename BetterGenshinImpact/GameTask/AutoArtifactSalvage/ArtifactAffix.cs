using System.Collections.Frozen;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    /// <summary>
    /// 圣遗物词条
    /// </summary>
    public class ArtifactAffix
    {
        public ArtifactAffix(ArtifactAffixType type, float value)
        {
            Type = type;
            Value = value;
        }

        public ArtifactAffixType Type { get; private set; }
        public float Value { get; private set; }
        public static FrozenDictionary<ArtifactAffixType, string> DefaultStrDic { get; } = new Dictionary<ArtifactAffixType, string>() {
            { ArtifactAffixType.ATK, "攻击力" },
            { ArtifactAffixType.ATKPercent, "攻击力" },
            { ArtifactAffixType.DEF, "防御力" },
            { ArtifactAffixType.DEFPercent, "防御力" },
            { ArtifactAffixType.HP, "生命值" },
            { ArtifactAffixType.HPPercent, "生命值" },
            { ArtifactAffixType.CRITRate, "暴击率" },
            { ArtifactAffixType.CRITDMG, "暴击伤害" },
            { ArtifactAffixType.ElementalMastery, "元素精通" },
            { ArtifactAffixType.EnergyRecharge, "元素充能效率" },
            { ArtifactAffixType.HealingBonus, "治疗加成" },
            { ArtifactAffixType.PhysicalDMGBonus, "物理伤害加成" },
            { ArtifactAffixType.PyroDMGBonus, "火元素伤害加成" },
            { ArtifactAffixType.HydroDMGBonus, "水元素伤害加成" },
            { ArtifactAffixType.DendroDMGBonus, "草元素伤害加成" },
            { ArtifactAffixType.ElectroDMGBonus, "雷元素伤害加成" },
            { ArtifactAffixType.AnemoDMGBonus, "风元素伤害加成" },
            { ArtifactAffixType.CryoDMGBonus, "冰元素伤害加成" },
            { ArtifactAffixType.GeoDMGBonus, "岩元素伤害加成" }
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
