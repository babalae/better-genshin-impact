using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainParam : BaseTaskParam<AutoDomainTask>
{
    /// <summary>仅本次运行使用。null 沿用游戏培养计划；JSON 数组元素为 material 和 target（目标库存）。</summary>
    public string? TrainingTargetsJson { get; set; }

    /// <summary>JS 使用 JSON.stringify([{material: "材料完整名称", target: 28}]) 传入。</summary>
    public void SetTrainingTargets(string json)
    {
        TrainingGuide.TrainingGuideCustomTargets.Parse(json);
        TrainingTargetsJson = json;
    }
    public int DomainRoundNum { get; set; }

    public string CombatStrategyPath { get; set; }

    // 刷副本使用的队伍名称
    public string PartyName { get; set; } = string.Empty;

    // 需要刷取的副本名称
    public string DomainName { get; set; } = string.Empty;

    // 需要刷取的副本名称
    public string SundaySelectedValue { get; set; } = string.Empty;

    // 结束后是否自动分解圣遗物
    public bool AutoArtifactSalvage { get; set; } = false;

    // 分解圣遗物的最大星级
    // 1~4
    public string MaxArtifactStar { get; set; } = "4";

    public bool SpecifyResinUse { get; set; } = false;

    // 使用树脂优先级
    public List<string> ResinPriorityList { get; set; } =
    [
        "浓缩树脂",
        "原粹树脂"
    ];
    // 使用原粹树脂刷取副本次数
    public int OriginalResinUseCount { get; set; } = 0;
    // 使用原粹树脂(20)刷取副本次数
    public int OriginalResin20UseCount { get; set; } = 0;

    // 使用原粹树脂(40)刷取副本次数
    public int OriginalResin40UseCount { get; set; } = 0;

    // 使用浓缩树脂刷取副本次数
    public int CondensedResinUseCount { get; set; } = 0;

    // 使用须臾树脂刷取副本次数
    public int TransientResinUseCount { get; set; } = 0;

    // 使用脆弱树脂刷取副本次数
    public int FragileResinUseCount { get; set; } = 0;

    /// <summary>
    /// 是否启用奖励名称识别。默认关闭。
    /// </summary>
    public bool RewardRecognitionEnabled { get; set; } = false;

    /// <summary>Training guide options inherit task settings at construction; JS may override any subset for this run.</summary>
    public bool TrainingGuideCalculateRunsEnabled { get; set; }

    /// <summary>0: reserve crafting bonuses; 1: ignore crafting bonuses. Other values normalize to 0.</summary>
    public int TrainingGuideRunPreference
    {
        get => _trainingGuideRunPreference;
        set => _trainingGuideRunPreference = value is 0 or 1 ? value : 0;
    }
    private int _trainingGuideRunPreference;

    /// <summary>Crafting bonus reserve percentage, clamped to 0 through 20.</summary>
    public int TrainingGuideCraftingBonusReservePercent
    {
        get => _trainingGuideCraftingBonusReservePercent;
        set => _trainingGuideCraftingBonusReservePercent = System.Math.Clamp(value, 0,
            TrainingGuide.TrainingGuideRunCalculator.MaxCraftingBonusReservePercent);
    }
    private int _trainingGuideCraftingBonusReservePercent;

    /// <summary>Use recognized rewards to update the remaining training plan.</summary>
    public bool TrainingGuideRewardRecognitionEnabled { get; set; }

    public bool TrainingGuideRewardFailureBudgetEnabled { get; set; }

    public bool TrainingGuideDiagnosticsEnabled { get; set; }

    /// <summary>Fallback domain after training targets are complete. Empty disables fallback.</summary>
    public string TrainingGuideFallbackDomainName { get; set; } = string.Empty;

    /// <summary>Sunday or limited-time reward selection for the fallback domain.</summary>
    public string TrainingGuideFallbackSundaySelectedValue { get; set; } = string.Empty;

    public bool ShouldRecognizeRewards() => RewardRecognitionEnabled ||
        (TrainingTargetsJson != null && TrainingGuideRewardRecognitionEnabled) ||
        (DomainName == AutoDomainTask.TrainingGuideOption &&
         TrainingGuideCalculateRunsEnabled && TrainingGuideRewardRecognitionEnabled);

    public AutoDomainParam(int domainRoundNum, string path) : base(null, null)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }

        CombatStrategyPath = path;
        SetDefault();
    }

    public void SetDefault()
    {
        var config = TaskContext.Instance().Config.AutoDomainConfig;
        PartyName = config.PartyName;
        DomainName = config.DomainName;
        SundaySelectedValue = config.SundaySelectedValue;
        AutoArtifactSalvage = config.AutoArtifactSalvage;
        MaxArtifactStar = TaskContext.Instance().Config.AutoArtifactSalvageConfig.MaxArtifactStar;
        ResinPriorityList = new List<string>(config.ResinPriorityList);
        OriginalResinUseCount = config.OriginalResinUseCount;
        CondensedResinUseCount = config.CondensedResinUseCount;
        TransientResinUseCount = config.TransientResinUseCount;
        FragileResinUseCount = config.FragileResinUseCount;
        SpecifyResinUse = config.SpecifyResinUse;
        OriginalResin20UseCount = config.OriginalResin20UseCount;
        OriginalResin40UseCount = config.OriginalResin40UseCount;
        RewardRecognitionEnabled = config.RewardRecognitionEnabled;
        TrainingGuideCalculateRunsEnabled = config.DevelopmentGuideCalculateRunsEnabled;
        TrainingGuideRunPreference = config.DevelopmentGuideRunPreference;
        TrainingGuideCraftingBonusReservePercent = config.DevelopmentGuideCraftingBonusReservePercent;
        TrainingGuideRewardRecognitionEnabled = config.DevelopmentGuideRewardRecognitionEnabled;
        TrainingGuideRewardFailureBudgetEnabled = config.TrainingGuideRewardFailureBudgetEnabled;
        TrainingGuideDiagnosticsEnabled = config.TrainingGuideDiagnosticsEnabled;
        TrainingGuideFallbackDomainName = config.DevelopmentGuideFallbackDomainName;
        TrainingGuideFallbackSundaySelectedValue = config.DevelopmentGuideFallbackSundaySelectedValue;
    }

    public AutoDomainParam(int domainRoundNum = 0) : base(null, null)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }

        CombatStrategyPath = SetCombatStrategyPath();
        SetDefault();
    }

    /// <summary>  
    /// 设置战斗策略路径
    /// </summary>  
    /// <param name="strategyName">策略名称</param>  
    public string SetCombatStrategyPath(string? strategyName = null)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            strategyName = TaskContext.Instance().Config.AutoFightConfig.StrategyName;
        }

        if ("根据队伍自动选择".Equals(strategyName))
        {
            return Global.Absolute(@"User\AutoFight\");
        }
        else if (AutoFightParam.ComboStrategyName.Equals(strategyName))
        {
            return AutoFightParam.ComboStrategyName;
        }
        else
        {
            return Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
        }
    }

    public void SetResinPriorityList(params string[] priorities)
    {
        ResinPriorityList.Clear();
        ResinPriorityList.AddRange(priorities);
    }
}
