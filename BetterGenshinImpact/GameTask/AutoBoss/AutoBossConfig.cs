using BetterGenshinImpact.GameTask.Common.Resin;
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
    /// <summary>统一树脂设置；为空时在读取配置后由旧字段迁移。</summary>
    [ObservableProperty]
    private ResinUsage? _resinUsage;

    [ObservableProperty]
    private string _bossName = string.Empty;

    [ObservableProperty]
    private string _strategyName = "根据队伍自动选择";

    [ObservableProperty]
    private string _teamName = string.Empty;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _specifyRunCount = false;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _runCount = 1;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _useTransientResin = false;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
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

    // 关闭指定讨伐次数时不再清空补充开关，刷至耗尽也允许补充。

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

    /// <summary>仅在尚无统一设置时，从旧字段迁移一次；已有新设置始终优先。</summary>
    public void MigrateLegacyResinUsage()
    {
        if (ResinUsage is not null) return;
        ResinUsage = ResinUsageLegacyMapper.FromBoss(SpecifyRunCount, RunCount, UseTransientResin, UseFragileResin);
    }

    /// <summary>确保完成旧字段迁移并返回配置持有的设置；参数类读取后必须 Clone。</summary>
    /// <returns>配置持有的非空树脂设置。</returns>
    public ResinUsage EnsureResinUsage()
    {
        MigrateLegacyResinUsage();
        return ResinUsage!;
    }
}
