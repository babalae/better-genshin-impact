using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.ClearScript;

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
        FinishDetectConfig.BattleEndProgressBarColor = TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig
            .BattleEndProgressBarColor;
        FinishDetectConfig.BattleEndProgressBarColorTolerance = TaskContext.Instance().Config.AutoFightConfig
            .FinishDetectConfig.BattleEndProgressBarColorTolerance;

        GuardianAvatar = autoFightConfig.GuardianAvatar;
        GuardianCombatSkip = autoFightConfig.GuardianCombatSkip;
        SkipModel = autoFightConfig.SkipModel;
        GuardianAvatarHold = autoFightConfig.GuardianAvatarHold;
    }

    /// <summary>  
    /// 从JS请求参数构建任务参数  
    /// </summary>  
    /// <param name="config"></param>  
    /// <returns></returns>  
    public static AutoFightParam BuildFromSoloTaskConfig(object config)
    {
        var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
        if (taskSettingsPageViewModel == null)
        {
            throw new ArgumentNullException(nameof(taskSettingsPageViewModel), "内部视图模型对象为空");
        }

        var jsObject = (ScriptObject)config;

        // 获取战斗策略路径
        string strategyPath;
        var customStrategyName = ScriptObjectConverter.GetValue(jsObject, "strategyName", "");

        if (string.IsNullOrEmpty(customStrategyName))
        {
            // 未指定战斗策略参数，使用"根据队伍自动选择"  
            if (taskSettingsPageViewModel.GetFightStrategy("根据队伍自动选择", out strategyPath))
            {
                throw new InvalidOperationException("获取默认战斗策略失败");
            }
        }
        else
        {
            // 指定了战斗策略，直接拼接路径  
            strategyPath = Global.Absolute(@"User\AutoFight\" + customStrategyName + ".txt");

            // 验证文件是否存在  
            if (!File.Exists(strategyPath))
            {
                throw new InvalidOperationException($"战斗策略文件不存在: {strategyPath}");
            }
        }

        // 创建自定义配置对象  
        var autoFightConfig = TaskContext.Instance().Config.AutoFightConfig;

        // 从JS配置中获取参数并覆盖默认值  
        var customConfig = new AutoFightConfig
        {
            StrategyName = ScriptObjectConverter.GetValue(jsObject, "strategyName", autoFightConfig.StrategyName),
            // TeamNames = ScriptObjectConverter.GetValue(jsObject, "teamNames", autoFightConfig.TeamNames),
            FightFinishDetectEnabled = ScriptObjectConverter.GetValue(jsObject, "fightFinishDetectEnabled", autoFightConfig.FightFinishDetectEnabled),
            Timeout = ScriptObjectConverter.GetValue(jsObject, "timeout", autoFightConfig.Timeout),
            KazuhaPickupEnabled = ScriptObjectConverter.GetValue(jsObject, "kazuhaPickupEnabled", autoFightConfig.KazuhaPickupEnabled),
            ActionSchedulerByCd = ScriptObjectConverter.GetValue(jsObject, "actionSchedulerByCd", autoFightConfig.ActionSchedulerByCd),
            PickDropsAfterFightEnabled = ScriptObjectConverter.GetValue(jsObject, "pickDropsAfterFightEnabled", autoFightConfig.PickDropsAfterFightEnabled),
            PickDropsAfterFightSeconds = ScriptObjectConverter.GetValue(jsObject, "pickDropsAfterFightSeconds", autoFightConfig.PickDropsAfterFightSeconds),
            OnlyPickEliteDropsMode = ScriptObjectConverter.GetValue(jsObject, "onlyPickEliteDropsMode", autoFightConfig.OnlyPickEliteDropsMode),
            GuardianAvatar = ScriptObjectConverter.GetValue(jsObject, "guardianAvatar", autoFightConfig.GuardianAvatar),
            GuardianCombatSkip = ScriptObjectConverter.GetValue(jsObject, "guardianCombatSkip", autoFightConfig.GuardianCombatSkip),
            SkipModel = ScriptObjectConverter.GetValue(jsObject, "skipModel", autoFightConfig.SkipModel),
            GuardianAvatarHold = ScriptObjectConverter.GetValue(jsObject, "guardianAvatarHold", autoFightConfig.GuardianAvatarHold)
        };

        // 处理 finishDetectConfig 参数  
        var finishDetectConfigJs = ScriptObjectConverter.GetValue<object>(jsObject, "finishDetectConfig", null);
        if (finishDetectConfigJs != null)
        {
            var finishDetectJsObject = (ScriptObject)finishDetectConfigJs;

            customConfig.FinishDetectConfig.FastCheckEnabled = ScriptObjectConverter.GetValue(finishDetectJsObject, "fastCheckEnabled", autoFightConfig.FinishDetectConfig.FastCheckEnabled);
            customConfig.FinishDetectConfig.FastCheckParams = ScriptObjectConverter.GetValue(finishDetectJsObject, "fastCheckParams", autoFightConfig.FinishDetectConfig.FastCheckParams);
            customConfig.FinishDetectConfig.CheckEndDelay = ScriptObjectConverter.GetValue(finishDetectJsObject, "checkEndDelay", autoFightConfig.FinishDetectConfig.CheckEndDelay);
            customConfig.FinishDetectConfig.BeforeDetectDelay = ScriptObjectConverter.GetValue(finishDetectJsObject, "beforeDetectDelay", autoFightConfig.FinishDetectConfig.BeforeDetectDelay);
            customConfig.FinishDetectConfig.RotateFindEnemyEnabled = ScriptObjectConverter.GetValue(finishDetectJsObject, "rotateFindEnemyEnabled", autoFightConfig.FinishDetectConfig.RotateFindEnemyEnabled);
            // customConfig.FinishDetectConfig.BattleEndProgressBarColor = ScriptObjectConverter.GetValue(finishDetectJsObject, "battleEndProgressBarColor", autoFightConfig.FinishDetectConfig.BattleEndProgressBarColor);
            // customConfig.FinishDetectConfig.BattleEndProgressBarColorTolerance =
            //     ScriptObjectConverter.GetValue(finishDetectJsObject, "battleEndProgressBarColorTolerance", autoFightConfig.FinishDetectConfig.BattleEndProgressBarColorTolerance);
        }

        return new AutoFightParam(strategyPath, customConfig);
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
    public bool SkipModel = false;
    public bool GuardianAvatarHold = false;
}