using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.GameTask.AutoDomain;

[Serializable]
public partial class AutoDomainConfig : ObservableObject
{
    /// <summary>
    /// 战斗结束后延迟几秒再开始寻找石化古树，秒
    /// </summary>
    [ObservableProperty]
    private double _fightEndDelay = 5;

    /// <summary>
    /// 寻找古树时，短距离移动，用于识别速度过慢的计算机使用
    /// </summary>
    [ObservableProperty]
    private bool _shortMovement = false;

    /// <summary>
    /// 寻找古树时，短距离移动，用于识别速度过慢的计算机使用
    /// </summary>
    [ObservableProperty]
    private bool _walkToF = true;

    /// <summary>
    /// 寻找古树时，短距离移动的次数
    /// </summary>
    [ObservableProperty]
    private int _leftRightMoveTimes = 3;

    /// <summary>
    /// 自动吃药
    /// </summary>
    [ObservableProperty]
    private bool _autoEat = false;
    
    // 刷副本使用的队伍名称
    [ObservableProperty]
    private string _partyName = string.Empty;

    // 需要刷取的副本名称
    [ObservableProperty]
    private string _domainName = string.Empty;

    // 结束后是否自动分解圣遗物
    [ObservableProperty]
    private bool _autoArtifactSalvage = false;
    
    // 周日奖励序号
    [ObservableProperty]
    private string _sundaySelectedValue = string.Empty;
    
    // 指定树脂的使用次数
    [ObservableProperty]
    private bool _specifyResinUse = false;
    
    // 自定义使用树脂优先级
    [ObservableProperty]
    private List<string> _resinPriorityList =
    [
        "浓缩树脂",
        "原粹树脂"
    ];
    
    // 使用原粹树脂刷取副本次数
    [ObservableProperty]
    private int _originalResinUseCount = 0;
    // 使用原粹树脂(20)刷取副本次数
    [ObservableProperty]
    private int _originalResin20UseCount = 0;

    // 使用原粹树脂(40)刷取副本次数
    [ObservableProperty]
    private int _originalResin40UseCount = 0;
    
    //使用浓缩树脂刷取副本次数
    [ObservableProperty]
    private int _condensedResinUseCount = 0;

    // 使用须臾树脂刷取副本次数
    [ObservableProperty]
    private int _transientResinUseCount = 0;
    
    // 使用脆弱树脂刷取副本次数
    [ObservableProperty]
    private int _fragileResinUseCount = 0;

    // 战斗死亡后重试次数
    [ObservableProperty]
    private int _reviveRetryCount = 3;

    /// <summary>
    /// 是否启用奖励名称识别。默认关闭。
    /// 开启后每轮领取奖励时会用 ONNX 图标匹配 + OCR 材料名双路识别奖励名称与数量，秘境结束打印汇总。
    /// </summary>
    [ObservableProperty]
    private bool _rewardRecognitionEnabled = false;

    /// <summary>
    /// 是否根据提升指南中的培养计划自动计算今日刷取次数。
    /// 这是培养计划扫描、动态调整和备选秘境逻辑的总开关。
    /// </summary>
    [ObservableProperty]
    private bool _developmentGuideCalculateRunsEnabled = true;

    /// <summary>
    /// 培养计划刷取偏好。0：预留合成天赋收益；1：不依赖合成天赋收益。
    /// </summary>
    [ObservableProperty]
    private int _developmentGuideRunPreference;

    /// <summary>
    /// 预留的合成天赋收益百分比，取值范围 0～20。
    /// </summary>
    [ObservableProperty]
    private int _developmentGuideCraftingBonusReservePercent = 7;

    partial void OnDevelopmentGuideRunPreferenceChanged(int value)
    {
        if (value is not (0 or 1))
        {
            DevelopmentGuideRunPreference = 0;
        }
    }

    partial void OnDevelopmentGuideCraftingBonusReservePercentChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, TrainingGuideRunCalculator.MaxCraftingBonusReservePercent);
        if (value != normalized)
        {
            DevelopmentGuideCraftingBonusReservePercent = normalized;
        }
    }

    /// <summary>
    /// 保留任务参数对普通奖励识别的控制；培养计划仅在总开关打开且选中提升指南时补充启用。
    /// </summary>
    public bool ShouldRecognizeRewards(string domainName, bool taskRewardRecognitionEnabled) =>
        taskRewardRecognitionEnabled ||
        (domainName == AutoDomainTask.TrainingGuideOption &&
         DevelopmentGuideCalculateRunsEnabled && DevelopmentGuideRewardRecognitionEnabled);

    /// <summary>
    /// 使用培养计划刷取时是否启用奖励识别。默认开启，用于后续动态调整刷取次数。
    /// 即使常规自动秘境奖励识别关闭，本选项开启时培养计划刷取仍会识别奖励。
    /// </summary>
    [ObservableProperty]
    private bool _developmentGuideRewardRecognitionEnabled = true;

    /// <summary>保留培养材料浮窗每次 OCR 尝试的原图和识别输入图，仅在独立任务设置页提供开关。</summary>
    [ObservableProperty]
    private bool _developmentGuideOcrDebugEnabled;
    /// <summary>仅由独立任务启动入口读取：遍历材料秘境进行 OCR，不战斗、不领奖。</summary>
    [ObservableProperty]
    private bool _trainingGuideOcrScanAllEnabled;
    // 培养计划完成或当天没有可刷取目标时使用的备选秘境
    [ObservableProperty]
    private string _developmentGuideFallbackDomainName = string.Empty;

    // 备选秘境在周日或限时活动中的奖励选择序号
    [ObservableProperty]
    private string _developmentGuideFallbackSundaySelectedValue = string.Empty;

}
