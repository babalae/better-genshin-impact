using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoMove;

/// <summary>
/// 自动前进任务配置
/// </summary>
[Serializable]
public partial class AutoMoveConfig : ObservableObject
{
    /// <summary>
    /// 启用自动前进
    /// </summary>
    [ObservableProperty]
    private bool _enableAutoWalk = false;

    /// <summary>
    /// 启用自动飞行
    /// </summary>
    [ObservableProperty]
    private bool _enableAutoFly = false;

    /// <summary>
    /// 启用自动游泳
    /// </summary>
    [ObservableProperty]
    private bool _enableAutoSwim = false;

    /// <summary>
    /// 启用自动爬山
    /// </summary>
    [ObservableProperty]
    private bool _enableAutoClimb = false;

    /// <summary>
    /// 夜兰自动E
    /// </summary>
    [ObservableProperty]
    private bool _enableYelanAutoE = false;

    /// <summary>
    /// 夜兰E技能间隔（秒）
    /// </summary>
    [ObservableProperty]
    private int _yelanEInterval = 3;

    /// <summary>
    /// 智能路径选择
    /// </summary>
    [ObservableProperty]
    private bool _enableSmartPathing = true;

    /// <summary>
    /// 避障检测
    /// </summary>
    [ObservableProperty]
    private bool _enableObstacleAvoidance = true;

    /// <summary>
    /// 自动调整视角
    /// </summary>
    [ObservableProperty]
    private bool _enableAutoViewAdjust = false;
}