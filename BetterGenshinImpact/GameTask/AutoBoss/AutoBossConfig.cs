using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using BetterGenshinImpact.GameTask.Common.Reward;

namespace BetterGenshinImpact.GameTask.AutoBoss;

/// <summary>
/// 自动首领讨伐的持久化配置，由独立任务页使用。
/// </summary>
[Serializable]
public partial class AutoBossConfig : ObservableObject
{
    [ObservableProperty]
    private string _bossName = string.Empty;

    [ObservableProperty]
    private string _strategyName = "根据队伍自动选择";

    [ObservableProperty]
    private string _teamName = string.Empty;

    [ObservableProperty]
    private bool _specifyRunCount = false;

    [ObservableProperty]
    private int _runCount = 1;

    [ObservableProperty]
    private bool _useTransientResin = false;

    [ObservableProperty]
    private bool _useFragileResin = false;

    [ObservableProperty]
    private int _reviveRetryCount = 3;

    [ObservableProperty]
    private bool _returnToStatueAfterEachRound = false;

    /// <summary>
    /// 是否启用奖励名称识别。默认关闭。
    /// </summary>
    [ObservableProperty]
    private bool _rewardRecognitionEnabled = false;
    
    /// <summary>
    /// 战斗超时
    /// </summary>
    [ObservableProperty]
    private int _timeout  = 240;

    /// <summary>
    /// 材料目标列表：按物品名与数量提前停止。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TargetItem> _targetItems = [];

    /// <summary>
    /// 关闭指定讨伐次数时清空补充树脂开关，避免树脂耗尽模式误用须臾或脆弱树脂。
    /// </summary>
    /// <param name="value">新的指定讨伐次数开关状态。</param>
    partial void OnSpecifyRunCountChanged(bool value)
    {
        if (value)
        {
            return;
        }

        UseTransientResin = false;
        UseFragileResin = false;
    }

    partial void OnRunCountChanged(int value)
    {
        if (value < 1)
        {
            RunCount = 1;
        }
    }

    partial void OnReviveRetryCountChanged(int value)
    {
        if (value < 0)
        {
            ReviveRetryCount = 0;
        }
    }
}
