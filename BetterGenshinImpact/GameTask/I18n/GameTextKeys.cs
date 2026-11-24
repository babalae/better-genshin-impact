namespace BetterGenshinImpact.GameTask.I18n;

/// <summary>
/// 游戏内识别文本的 key，取值见 <see cref="GameTextService"/>，文本表位于 GameTask/I18n/{language}.json。
/// key 为英文语义标识，格式为 &lt;模块&gt;.&lt;名称&gt;；注释为 zh-Hans 的值。
/// 新增 key 时必须同时在 zh-Hans.json 中登记。
/// </summary>
public static class GameTextKeys
{
    public static class Common
    {
        /// <summary>复苏</summary>
        public const string Revive = "Common.Revive";

        /// <summary>委托</summary>
        public const string Commissions = "Common.Commissions";

        /// <summary>跳过</summary>
        public const string Skip = "Common.Skip";

        /// <summary>点击任意位置关闭</summary>
        public const string ClickAnywhereToClose = "Common.ClickAnywhereToClose";

        /// <summary>使用</summary>
        public const string Use = "Common.Use";

        /// <summary>取消</summary>
        public const string Cancel = "Common.Cancel";
    }

    public static class Resin
    {
        /// <summary>原粹树脂</summary>
        public const string Original = "Resin.Original";

        /// <summary>浓缩树脂</summary>
        public const string Condensed = "Resin.Condensed";

        /// <summary>须臾树脂</summary>
        public const string Transient = "Resin.Transient";

        /// <summary>脆弱树脂</summary>
        public const string Fragile = "Resin.Fragile";

        /// <summary>补充原粹树脂</summary>
        public const string ReplenishOriginal = "Resin.ReplenishOriginal";

        /// <summary>数量不足</summary>
        public const string Insufficient = "Resin.Insufficient";
    }

    public static class Domain
    {
        /// <summary>(挑战|达成)</summary>
        public const string ChallengeCompleted = "Domain.ChallengeCompleted";

        /// <summary>(自动|退出)</summary>
        public const string AutoLeaving = "Domain.AutoLeaving";

        /// <summary>地脉异常</summary>
        public const string LeyLineDisorder = "Domain.LeyLineDisorder";

        /// <summary>匹配挑战</summary>
        public const string MatchingChallenge = "Domain.MatchingChallenge";

        /// <summary>快速编队</summary>
        public const string QuickSetup = "Domain.QuickSetup";

        /// <summary>限时全部开放</summary>
        public const string LimitedTimeFullyOpen = "Domain.LimitedTimeFullyOpen";

        /// <summary>限时开放</summary>
        public const string LimitedTimeOpen = "Domain.LimitedTimeOpen";

        /// <summary>单人挑战</summary>
        public const string SinglePlayerChallenge = "Domain.SinglePlayerChallenge";

        /// <summary>开始挑战</summary>
        public const string StartChallenge = "Domain.StartChallenge";

        /// <summary>是否仍要.*挑战.*秘境</summary>
        public const string RetryPrompt = "Domain.RetryPrompt";

        /// <summary>石化古树</summary>
        public const string PetrifiedTree = "Domain.PetrifiedTree";
    }

    public static class Artifact
    {
        /// <summary>快速选择</summary>
        public const string QuickSelect = "Artifact.QuickSelect";

        /// <summary>1星圣遗物</summary>
        public const string Star1 = "Artifact.Star1";

        /// <summary>2星圣遗物</summary>
        public const string Star2 = "Artifact.Star2";

        /// <summary>3星圣遗物</summary>
        public const string Star3 = "Artifact.Star3";

        /// <summary>4星圣遗物</summary>
        public const string Star4 = "Artifact.Star4";

        public static class Affix
        {
            /// <summary>攻击力</summary>
            public const string ATK = "Artifact.Affix.ATK";

            /// <summary>防御力</summary>
            public const string DEF = "Artifact.Affix.DEF";

            /// <summary>生命值</summary>
            public const string HP = "Artifact.Affix.HP";

            /// <summary>暴击率</summary>
            public const string CRITRate = "Artifact.Affix.CRITRate";

            /// <summary>暴击伤害</summary>
            public const string CRITDMG = "Artifact.Affix.CRITDMG";

            /// <summary>元素精通</summary>
            public const string ElementalMastery = "Artifact.Affix.ElementalMastery";

            /// <summary>元素充能效率</summary>
            public const string EnergyRecharge = "Artifact.Affix.EnergyRecharge";

            /// <summary>治疗加成</summary>
            public const string HealingBonus = "Artifact.Affix.HealingBonus";

            /// <summary>物理伤害加成</summary>
            public const string PhysicalDMGBonus = "Artifact.Affix.PhysicalDMGBonus";

            /// <summary>火元素伤害加成</summary>
            public const string PyroDMGBonus = "Artifact.Affix.PyroDMGBonus";

            /// <summary>水元素伤害加成</summary>
            public const string HydroDMGBonus = "Artifact.Affix.HydroDMGBonus";

            /// <summary>草元素伤害加成</summary>
            public const string DendroDMGBonus = "Artifact.Affix.DendroDMGBonus";

            /// <summary>雷元素伤害加成</summary>
            public const string ElectroDMGBonus = "Artifact.Affix.ElectroDMGBonus";

            /// <summary>风元素伤害加成</summary>
            public const string AnemoDMGBonus = "Artifact.Affix.AnemoDMGBonus";

            /// <summary>冰元素伤害加成</summary>
            public const string CryoDMGBonus = "Artifact.Affix.CryoDMGBonus";

            /// <summary>岩元素伤害加成</summary>
            public const string GeoDMGBonus = "Artifact.Affix.GeoDMGBonus";
        }
    }

    public static class Fishing
    {
        /// <summary>钓鱼</summary>
        public const string Fish = "Fishing.Fish";

        /// <summary>上钩</summary>
        public const string Bite = "Fishing.Bite";
    }

    public static class Map
    {
        public static class Area
        {
            /// <summary>蒙德</summary>
            public const string Mondstadt = "Map.Area.Mondstadt";

            /// <summary>璃月</summary>
            public const string Liyue = "Map.Area.Liyue";

            /// <summary>稻妻</summary>
            public const string Inazuma = "Map.Area.Inazuma";

            /// <summary>须弥</summary>
            public const string Sumeru = "Map.Area.Sumeru";

            /// <summary>枫丹</summary>
            public const string Fontaine = "Map.Area.Fontaine";

            /// <summary>纳塔</summary>
            public const string Natlan = "Map.Area.Natlan";

            /// <summary>挪德卡莱</summary>
            public const string NodKrai = "Map.Area.NodKrai";

            /// <summary>至冬</summary>
            public const string Snezhnaya = "Map.Area.Snezhnaya";

            /// <summary>尘歌壶</summary>
            public const string SereniteaPot = "Map.Area.SereniteaPot";
        }
    }

    public static class Rewards
    {
        /// <summary>今日奖励已领取</summary>
        public const string DailyClaimed = "Rewards.DailyClaimed";

        /// <summary>每日委托奖励</summary>
        public const string DailyCommissionRewards = "Rewards.DailyCommissionRewards";
    }

    public static class BattlePass
    {
        /// <summary>一键</summary>
        public const string OneClick = "BattlePass.OneClick";

        /// <summary>领取</summary>
        public const string Claim = "BattlePass.Claim";
    }

    public static class Guild
    {
        /// <summary>凯瑟琳</summary>
        public const string Katheryne = "Guild.Katheryne";

        /// <summary>每日</summary>
        public const string Daily = "Guild.Daily";

        /// <summary>探索</summary>
        public const string Expedition = "Guild.Expedition";
    }

    public static class Crafting
    {
        /// <summary>合成</summary>
        public const string Craft = "Crafting.Craft";
    }

    public static class SereniteaPot
    {
        /// <summary>阿圆</summary>
        public const string Ayuan = "SereniteaPot.Ayuan";

        /// <summary>壶灵</summary>
        public const string Tubby = "SereniteaPot.Tubby";

        /// <summary>&lt;壶灵&gt;</summary>
        public const string TubbyTitle = "SereniteaPot.TubbyTitle";

        /// <summary>信任</summary>
        public const string Trust = "SereniteaPot.Trust";

        /// <summary>洞天百宝</summary>
        public const string RealmDepot = "SereniteaPot.RealmDepot";
    }
}
