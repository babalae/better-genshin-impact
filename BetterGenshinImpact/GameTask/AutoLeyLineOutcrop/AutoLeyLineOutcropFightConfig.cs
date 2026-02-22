using BetterGenshinImpact.GameTask.AutoFight;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

[Serializable]
public partial class AutoLeyLineOutcropFightConfig : ObservableObject
{
    [ObservableProperty] private string _strategyName = "";

    /// <summary>
    /// 英文逗号分割，强制指定队伍角色。
    /// </summary>
    [ObservableProperty] private string _teamNames = "";

    /// <summary>
    /// 检测战斗结束。
    /// </summary>
    [ObservableProperty] private bool _fightFinishDetectEnabled = true;

    /// <summary>
    /// 根据技能CD优化出招人员。
    /// </summary>
    [ObservableProperty] private string _actionSchedulerByCd = "";

    [Serializable]
    public partial class FightFinishDetectConfig : ObservableObject
    {
        [ObservableProperty] private string _battleEndProgressBarColor = "";
        [ObservableProperty] private string _battleEndProgressBarColorTolerance = "";
        [ObservableProperty] private bool _fastCheckEnabled = false;
        [ObservableProperty] private bool _rotateFindEnemyEnabled = false;
        [ObservableProperty] private string _fastCheckParams = "";
        [ObservableProperty] private string _checkEndDelay = "";
        [ObservableProperty] private string _beforeDetectDelay = "";
        [ObservableProperty] private int _rotaryFactor = 10;
        [ObservableProperty] private bool _isFirstCheck = false;
        [ObservableProperty] private bool _checkBeforeBurst = false;
    }

    [ObservableProperty] private FightFinishDetectConfig _finishDetectConfig = new();
    [ObservableProperty] private string _guardianAvatar = string.Empty;
    [ObservableProperty] private bool _guardianCombatSkip = false;
    [ObservableProperty] private bool _guardianAvatarHold = false;
    [ObservableProperty] private bool _burstEnabled = false;
    [ObservableProperty] private bool _swimmingEnabled = false;
    [ObservableProperty] private int _timeout = 120;

    public void CopyFromAutoFightConfig(AutoFightConfig source)
    {
        StrategyName = source.StrategyName;
        TeamNames = source.TeamNames;
        FightFinishDetectEnabled = source.FightFinishDetectEnabled;
        ActionSchedulerByCd = source.ActionSchedulerByCd;
        GuardianAvatar = source.GuardianAvatar;
        GuardianCombatSkip = source.GuardianCombatSkip;
        GuardianAvatarHold = source.GuardianAvatarHold;
        BurstEnabled = source.BurstEnabled;
        SwimmingEnabled = source.SwimmingEnabled;
        Timeout = source.Timeout;

        FinishDetectConfig.BattleEndProgressBarColor = source.FinishDetectConfig.BattleEndProgressBarColor;
        FinishDetectConfig.BattleEndProgressBarColorTolerance = source.FinishDetectConfig.BattleEndProgressBarColorTolerance;
        FinishDetectConfig.FastCheckEnabled = source.FinishDetectConfig.FastCheckEnabled;
        FinishDetectConfig.RotateFindEnemyEnabled = source.FinishDetectConfig.RotateFindEnemyEnabled;
        FinishDetectConfig.FastCheckParams = source.FinishDetectConfig.FastCheckParams;
        FinishDetectConfig.CheckEndDelay = source.FinishDetectConfig.CheckEndDelay;
        FinishDetectConfig.BeforeDetectDelay = source.FinishDetectConfig.BeforeDetectDelay;
        FinishDetectConfig.RotaryFactor = source.FinishDetectConfig.RotaryFactor;
        FinishDetectConfig.IsFirstCheck = source.FinishDetectConfig.IsFirstCheck;
        FinishDetectConfig.CheckBeforeBurst = source.FinishDetectConfig.CheckBeforeBurst;
    }

    public AutoFightConfig ToAutoFightConfig()
    {
        var config = new AutoFightConfig
        {
            StrategyName = StrategyName,
            TeamNames = TeamNames,
            FightFinishDetectEnabled = FightFinishDetectEnabled,
            ActionSchedulerByCd = ActionSchedulerByCd,
            GuardianAvatar = GuardianAvatar,
            GuardianCombatSkip = GuardianCombatSkip,
            GuardianAvatarHold = GuardianAvatarHold,
            BurstEnabled = BurstEnabled,
            SwimmingEnabled = SwimmingEnabled,
            Timeout = Timeout,

            // 地脉花战斗不启用战斗后拾取逻辑。
            PickDropsAfterFightEnabled = false,
            PickDropsAfterFightSeconds = 0,
            KazuhaPickupEnabled = false,
            QinDoublePickUp = false,
            OnlyPickEliteDropsMode = "DisableAutoPickupForNonElite",
            KazuhaPartyName = string.Empty,
            BattleThresholdForLoot = null
        };

        config.FinishDetectConfig = new AutoFightConfig.FightFinishDetectConfig
        {
            BattleEndProgressBarColor = FinishDetectConfig.BattleEndProgressBarColor,
            BattleEndProgressBarColorTolerance = FinishDetectConfig.BattleEndProgressBarColorTolerance,
            FastCheckEnabled = FinishDetectConfig.FastCheckEnabled,
            RotateFindEnemyEnabled = FinishDetectConfig.RotateFindEnemyEnabled,
            FastCheckParams = FinishDetectConfig.FastCheckParams,
            CheckEndDelay = FinishDetectConfig.CheckEndDelay,
            BeforeDetectDelay = FinishDetectConfig.BeforeDetectDelay,
            RotaryFactor = FinishDetectConfig.RotaryFactor,
            IsFirstCheck = FinishDetectConfig.IsFirstCheck,
            CheckBeforeBurst = FinishDetectConfig.CheckBeforeBurst
        };

        return config;
    }
}
