using BetterGenshinImpact.GameTask.Common.Resin;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

[Serializable]
public partial class AutoLeyLineOutcropConfig : ObservableObject
{
    /// <summary>统一树脂设置；为空时在读取配置后由旧字段迁移。</summary>
    [ObservableProperty]
    private ResinUsage? _resinUsage;

    public AutoLeyLineOutcropConfig()
    {
        AttachFightConfigEvents(_fightConfig);
    }

    [ObservableProperty]
    private string _leyLineOutcropType = "启示之花";

    [ObservableProperty]
    private string _country = "蒙德";

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _isResinExhaustionMode = false;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _openModeCountMin = false;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _count = 6;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _useTransientResin = false;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _useFragileResin = false;

    [ObservableProperty]
    private string _team = string.Empty;

    [ObservableProperty]
    private string _friendshipTeam = string.Empty;

    [ObservableProperty]
    private int _timeout = 120;

    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _isGoToSynthesizer = false;

    /// <summary>
    /// 是否在领取地脉花奖励后扫描周围掉落物光柱。
    /// </summary>
    [ObservableProperty]
    private bool _scanDropsAfterRewardEnabled = false;

    /// <summary>
    /// 领取奖励后扫描掉落物的最长时长，单位为秒。
    /// </summary>
    [ObservableProperty]
    private int _scanDropsAfterRewardSeconds = 12;

    [ObservableProperty]
    private AutoLeyLineOutcropFightConfig _fightConfig = new();

    partial void OnFightConfigChanged(AutoLeyLineOutcropFightConfig value)
    {
        AttachFightConfigEvents(value);
        OnPropertyChanged(nameof(FightConfig));
    }

    private void AttachFightConfigEvents(AutoLeyLineOutcropFightConfig? config)
    {
        if (config == null)
        {
            return;
        }

        config.PropertyChanged -= OnFightConfigPropertyChanged;
        config.PropertyChanged += OnFightConfigPropertyChanged;
        config.FinishDetectConfig.PropertyChanged -= OnFightConfigPropertyChanged;
        config.FinishDetectConfig.PropertyChanged += OnFightConfigPropertyChanged;
    }

    private void OnFightConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FightConfig));
    }

    /// <summary>仅在尚无统一设置时，从旧字段迁移一次；已有新设置始终优先。</summary>
    public void MigrateLegacyResinUsage()
    {
        if (ResinUsage is not null) return;
        ResinUsage = ResinUsageLegacyMapper.FromLeyLine(IsResinExhaustionMode, OpenModeCountMin, Count,
            UseTransientResin, UseFragileResin);
    }

    /// <summary>确保完成旧字段迁移并返回配置持有的设置；参数类读取后必须 Clone。</summary>
    /// <returns>配置持有的非空树脂设置。</returns>
    public ResinUsage EnsureResinUsage()
    {
        MigrateLegacyResinUsage();
        return ResinUsage!;
    }
}
