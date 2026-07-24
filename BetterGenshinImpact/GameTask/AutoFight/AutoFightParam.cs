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
        public bool SkipFightEndCheckWhenEnemyVisible = false;
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
        FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible = autoFightConfig.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible;
        EnableCombatTargeting = autoFightConfig.EnableCombatTargeting;
        TargetingDetectionInterval = autoFightConfig.TargetingDetectionInterval;
        DrawRecognitionResults = autoFightConfig.DrawRecognitionResults;
        LockLostWaitTime = autoFightConfig.LockLostWaitTime;
        DamageNumberRecognitionMode = autoFightConfig.DamageNumberRecognitionMode;
        QinDoublePickUp = autoFightConfig.QinDoublePickUp;
        SwimmingEnabled = autoFightConfig.SwimmingEnabled;
        ExpBasedPickupEnabled = autoFightConfig.ExpBasedPickupEnabled;
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
    public bool SkipFightEndCheckWhenEnemyVisible { get; set; } = false;
    public bool BurstEnabled { get; set; } = false;
    
    public bool QinDoublePickUp { get; set; } = false;
    public static bool SwimmingEnabled  { get; set; } = false;
    public bool EnableCombatTargeting { get; set; } = false;
    public int TargetingDetectionInterval { get; set; } = 50;
    public bool DrawRecognitionResults { get; set; } = true;
    public double LockLostWaitTime { get; set; } = 0.5;
    public DamageNumberRecognitionMode DamageNumberRecognitionMode { get; set; } = DamageNumberRecognitionMode.Color;

    /// <summary>
    /// 基于经验值判断是否执行战后拾取
    /// </summary>
    public bool ExpBasedPickupEnabled { get; set; } = false;

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

    /// <summary>
    /// 解析策略文件路径，自动检测 .json 或 .txt 扩展名。
    /// 优先检测 .json，未命中则回退 .txt。
    /// </summary>
    /// <param name="strategyName">策略名称（不含扩展名）</param>
    /// <returns>(完整路径, 类型标识: "json" / "txt")</returns>
    public static (string path, string type) ResolveStrategyPath(string strategyName)
    {
        if ("根据队伍自动选择".Equals(strategyName))
        {
            var dir = Global.Absolute(@"User\AutoFight\");
            return (dir, "txt");
        }

        var baseDir = Global.Absolute(@"User\AutoFight\");

        // 优先检测 .json
        var jsonPath = System.IO.Path.Combine(baseDir, strategyName + ".json");
        if (System.IO.File.Exists(jsonPath))
        {
            return (jsonPath, "json");
        }

        // 回退 .txt
        var txtPath = System.IO.Path.Combine(baseDir, strategyName + ".txt");
        return (txtPath, "txt");
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
        FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible = autoFightConfig.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible;

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
        EnableCombatTargeting = autoFightConfig.EnableCombatTargeting;
        TargetingDetectionInterval = autoFightConfig.TargetingDetectionInterval;
        DrawRecognitionResults = autoFightConfig.DrawRecognitionResults;
        LockLostWaitTime = autoFightConfig.LockLostWaitTime;
        DamageNumberRecognitionMode = autoFightConfig.DamageNumberRecognitionMode;
        ExpBasedPickupEnabled = autoFightConfig.ExpBasedPickupEnabled;
    }
}