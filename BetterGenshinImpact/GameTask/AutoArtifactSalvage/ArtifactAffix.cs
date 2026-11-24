using BetterGenshinImpact.GameTask.I18n;
using System.Collections.Frozen;
using System.Collections.Generic;

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

        /// <summary>
        /// 词条类型 → 游戏文本 key（<see cref="GameTextKeys.Artifact.Affix"/>），百分比与固定值词条共用同一个名称
        /// </summary>
        public static FrozenDictionary<ArtifactAffixType, string> GameTextKeyDic { get; } = new Dictionary<ArtifactAffixType, string>() {
            { ArtifactAffixType.ATK, GameTextKeys.Artifact.Affix.ATK },
            { ArtifactAffixType.ATKPercent, GameTextKeys.Artifact.Affix.ATK },
            { ArtifactAffixType.DEF, GameTextKeys.Artifact.Affix.DEF },
            { ArtifactAffixType.DEFPercent, GameTextKeys.Artifact.Affix.DEF },
            { ArtifactAffixType.HP, GameTextKeys.Artifact.Affix.HP },
            { ArtifactAffixType.HPPercent, GameTextKeys.Artifact.Affix.HP },
            { ArtifactAffixType.CRITRate, GameTextKeys.Artifact.Affix.CRITRate },
            { ArtifactAffixType.CRITDMG, GameTextKeys.Artifact.Affix.CRITDMG },
            { ArtifactAffixType.ElementalMastery, GameTextKeys.Artifact.Affix.ElementalMastery },
            { ArtifactAffixType.EnergyRecharge, GameTextKeys.Artifact.Affix.EnergyRecharge },
            { ArtifactAffixType.HealingBonus, GameTextKeys.Artifact.Affix.HealingBonus },
            { ArtifactAffixType.PhysicalDMGBonus, GameTextKeys.Artifact.Affix.PhysicalDMGBonus },
            { ArtifactAffixType.PyroDMGBonus, GameTextKeys.Artifact.Affix.PyroDMGBonus },
            { ArtifactAffixType.HydroDMGBonus, GameTextKeys.Artifact.Affix.HydroDMGBonus },
            { ArtifactAffixType.DendroDMGBonus, GameTextKeys.Artifact.Affix.DendroDMGBonus },
            { ArtifactAffixType.ElectroDMGBonus, GameTextKeys.Artifact.Affix.ElectroDMGBonus },
            { ArtifactAffixType.AnemoDMGBonus, GameTextKeys.Artifact.Affix.AnemoDMGBonus },
            { ArtifactAffixType.CryoDMGBonus, GameTextKeys.Artifact.Affix.CryoDMGBonus },
            { ArtifactAffixType.GeoDMGBonus, GameTextKeys.Artifact.Affix.GeoDMGBonus }
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
