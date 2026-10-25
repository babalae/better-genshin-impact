using BetterGenshinImpact.GameTask.Common.Resin;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoDomain;

[Serializable]
public partial class AutoDomainConfig : ObservableObject
{
    /// <summary>统一树脂设置；为空时在读取配置后由旧字段迁移。</summary>
    [ObservableProperty]
    private ResinUsage? _resinUsage;

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
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _specifyResinUse = false;
    
    // 自定义使用树脂优先级
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private List<string> _resinPriorityList =
    [
        "浓缩树脂",
        "原粹树脂"
    ];
    
    // 使用原粹树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _originalResinUseCount = 0;
    // 使用原粹树脂(20)刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _originalResin20UseCount = 0;

    // 使用原粹树脂(40)刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _originalResin40UseCount = 0;
    
    //使用浓缩树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _condensedResinUseCount = 0;

    // 使用须臾树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _transientResinUseCount = 0;
    
    // 使用脆弱树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
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


    /// <summary>仅在尚无统一设置时，从旧字段迁移一次；已有新设置始终优先。</summary>
    public void MigrateLegacyResinUsage()
    {
        if (ResinUsage is not null) return;
        ResinUsage = ResinUsageLegacyMapper.FromDomain(SpecifyResinUse, OriginalResinUseCount, CondensedResinUseCount,
            TransientResinUseCount, FragileResinUseCount, OriginalResin20UseCount, OriginalResin40UseCount, out var warning);
        if (warning is not null) Serilog.Log.Warning("{Warning}", warning);
    }

    /// <summary>确保完成旧字段迁移并返回配置持有的设置；参数类读取后必须 Clone。</summary>
    /// <returns>配置持有的非空树脂设置。</returns>
    public ResinUsage EnsureResinUsage()
    {
        MigrateLegacyResinUsage();
        return ResinUsage!;
    }
}