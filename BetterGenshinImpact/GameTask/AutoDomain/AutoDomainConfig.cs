using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

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
}