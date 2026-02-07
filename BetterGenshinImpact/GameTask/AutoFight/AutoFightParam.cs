using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;






public class AutoFightParam : BaseTaskParam<AutoFightTask>
{
    public class FightFinishDetectConfig
    {
        public string BattleEndProgressBarColor { get; set; } = "";

        public string BattleEndProgressBarColorTolerance { get; set; } = "";
        public bool FastCheckEnabled = false;
        public string FastCheckParams = "";
        public string CheckEndDelay = "";
        public string BeforeDetectDelay = "";
        public bool RotateFindEnemyEnabled = false;
    }

    public AutoFightParam(string path, AutoFightConfig autoFightConfig) : base(null, null)
    {
        CombatStrategyPath = path;
        Timeout = autoFightConfig.Timeout;
        FightFinishDetectEnabled = autoFightConfig.FightFinishDetectEnabled;
        PickDropsAfterFightEnabled = autoFightConfig.PickDropsAfterFightEnabled;
        PickDropsAfterFightSeconds = autoFightConfig.PickDropsAfterFightSeconds;
        KazuhaPickupEnabled = autoFightConfig.KazuhaPickupEnabled;
        ActionSchedulerByCd = autoFightConfig.ActionSchedulerByCd;

        FinishDetectConfig.FastCheckEnabled = autoFightConfig.FinishDetectConfig.FastCheckEnabled;
        FinishDetectConfig.FastCheckParams = autoFightConfig.FinishDetectConfig.FastCheckParams;
        FinishDetectConfig.CheckEndDelay = autoFightConfig.FinishDetectConfig.CheckEndDelay;
        FinishDetectConfig.BeforeDetectDelay = autoFightConfig.FinishDetectConfig.BeforeDetectDelay;
        FinishDetectConfig.RotateFindEnemyEnabled = autoFightConfig.FinishDetectConfig.RotateFindEnemyEnabled;


        KazuhaPartyName = autoFightConfig.KazuhaPartyName;
        OnlyPickEliteDropsMode = autoFightConfig.OnlyPickEliteDropsMode;
        BattleThresholdForLoot = autoFightConfig.BattleThresholdForLoot ?? BattleThresholdForLoot;
        //下面参数固定，只取自动战斗里面的
        FinishDetectConfig.BattleEndProgressBarColor = TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig.BattleEndProgressBarColor;
        FinishDetectConfig.BattleEndProgressBarColorTolerance = TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig.BattleEndProgressBarColorTolerance;

        GuardianAvatar = autoFightConfig.GuardianAvatar;
        GuardianCombatSkip = autoFightConfig.GuardianCombatSkip;
        GuardianAvatarHold = autoFightConfig.GuardianAvatarHold;
        BurstEnabled = autoFightConfig.BurstEnabled;
        
        CheckBeforeBurst = autoFightConfig.FinishDetectConfig.CheckBeforeBurst;
        IsFirstCheck = autoFightConfig.FinishDetectConfig.IsFirstCheck;
        RotaryFactor = autoFightConfig.FinishDetectConfig.RotaryFactor;
        QinDoublePickUp = autoFightConfig.QinDoublePickUp;
        SwimmingEnabled = autoFightConfig.SwimmingEnabled;
    }

    public FightFinishDetectConfig FinishDetectConfig { get; set; } = new();

    public string CombatStrategyPath { get; set; }

    public bool FightFinishDetectEnabled { get; set; } = false;
    public bool PickDropsAfterFightEnabled { get; set; } = false;
    public int PickDropsAfterFightSeconds { get; set; } = 15;
    public int BattleThresholdForLoot { get; set; } = -1;
    public int Timeout { get; set; } = 120;

    public bool KazuhaPickupEnabled = true;
    public string ActionSchedulerByCd = "";
    public string KazuhaPartyName;
    public string OnlyPickEliteDropsMode = "";
    public string GuardianAvatar { get; set; } = string.Empty;
    public bool GuardianCombatSkip { get; set; } = false;
    public bool GuardianAvatarHold = false;
    
    public bool CheckBeforeBurst { get; set; } = false;
    public bool IsFirstCheck { get; set; } = true;    
    public int RotaryFactor { get; set; } = 10;
    public bool BurstEnabled { get; set; } = false;
    
    public bool QinDoublePickUp { get; set; } = false;
    public static bool SwimmingEnabled  { get; set; } = false;

    public AutoFightParam(string? strategyName = null) : base(null, null)
    {
        SetCombatStrategyPath(strategyName);
        SetDefault();
    }

    /// <summary>  
    /// 设置战斗策略路径
    /// </summary>  
    /// <param name="strategyName">策略名称</param>  
    public void SetCombatStrategyPath(string? strategyName = null)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            strategyName = TaskContext.Instance().Config.AutoFightConfig.StrategyName;
        }

        if ("根据队伍自动选择".Equals(strategyName))
        {
            CombatStrategyPath =  Global.Absolute(@"User\AutoFight\");
        }
        else
        {
            CombatStrategyPath =  Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
        }
    }

    public void SetDefault()
    {
        var autoFightConfig = TaskContext.Instance().Config.AutoFightConfig;
        Timeout = autoFightConfig.Timeout;
        FightFinishDetectEnabled = autoFightConfig.FightFinishDetectEnabled;
        PickDropsAfterFightEnabled = autoFightConfig.PickDropsAfterFightEnabled;
        PickDropsAfterFightSeconds = autoFightConfig.PickDropsAfterFightSeconds;
        KazuhaPickupEnabled = autoFightConfig.KazuhaPickupEnabled;
        ActionSchedulerByCd = autoFightConfig.ActionSchedulerByCd;

        FinishDetectConfig.FastCheckEnabled = autoFightConfig.FinishDetectConfig.FastCheckEnabled;
        FinishDetectConfig.FastCheckParams = autoFightConfig.FinishDetectConfig.FastCheckParams;
        FinishDetectConfig.CheckEndDelay = autoFightConfig.FinishDetectConfig.CheckEndDelay;
        FinishDetectConfig.BeforeDetectDelay = autoFightConfig.FinishDetectConfig.BeforeDetectDelay;
        FinishDetectConfig.RotateFindEnemyEnabled = autoFightConfig.FinishDetectConfig.RotateFindEnemyEnabled;


        KazuhaPartyName = autoFightConfig.KazuhaPartyName;
        OnlyPickEliteDropsMode = autoFightConfig.OnlyPickEliteDropsMode;
        BattleThresholdForLoot = autoFightConfig.BattleThresholdForLoot ?? BattleThresholdForLoot;
        //下面参数固定，只取自动战斗里面的
        FinishDetectConfig.BattleEndProgressBarColor = autoFightConfig.FinishDetectConfig.BattleEndProgressBarColor;
        FinishDetectConfig.BattleEndProgressBarColorTolerance = autoFightConfig.FinishDetectConfig.BattleEndProgressBarColorTolerance;

        GuardianAvatar = autoFightConfig.GuardianAvatar;
        GuardianCombatSkip = autoFightConfig.GuardianCombatSkip;
        GuardianAvatarHold = autoFightConfig.GuardianAvatarHold;
        SwimmingEnabled = autoFightConfig.SwimmingEnabled;
        QinDoublePickUp = autoFightConfig.QinDoublePickUp;
    }
}