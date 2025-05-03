using CommunityToolkit.Mvvm.ComponentModel;
using System;

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
}