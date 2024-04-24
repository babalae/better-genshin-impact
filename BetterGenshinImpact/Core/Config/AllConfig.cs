using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.QuickTeleport;
using BetterGenshinImpact.Service.Notification;
using CommunityToolkit.Mvvm.ComponentModel;
using Fischless.GameCapture;
using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     更好的原神配置
/// </summary>
[Serializable]
public partial class AllConfig : ObservableObject
{
    /// <summary>
    ///     窗口捕获的方式
    /// </summary>
    [ObservableProperty]
    private string _captureMode = CaptureModes.BitBlt.ToString();

    /// <summary>
    ///     详细的错误日志
    /// </summary>
    [ObservableProperty]
    private bool _detailedErrorLogs;

    /// <summary>
    ///     不展示新版本提示的最新版本
    /// </summary>
    [ObservableProperty]
    private string _notShowNewVersionNoticeEndVersion = "";

    ///// <summary>
    ///// 窗口捕获帧数/触发器触发频率
    ///// </summary>
    //[ObservableProperty] private int _frameRate = 30;

    /// <summary>
    ///     触发器触发频率(ms)
    /// </summary>
    [ObservableProperty]
    private int _triggerInterval = 50;

    /// <summary>
    ///     WGC使用位图缓存
    ///     高帧率情况下，可能会导致卡顿
    ///     云原神可能会出现黑屏
    /// </summary>
    [ObservableProperty]
    private bool _wgcUseBitmapCache = true;

    /// <summary>
    ///     遮罩窗口配置
    /// </summary>
    public MaskWindowConfig MaskWindowConfig { get; set; } = new();

    /// <summary>
    ///     通用配置
    /// </summary>
    public CommonConfig CommonConfig { get; set; } = new();

    /// <summary>
    ///     原神启动配置
    /// </summary>
    public GenshinStartConfig GenshinStartConfig { get; set; } = new();

    /// <summary>
    ///     自动拾取配置
    /// </summary>
    public AutoPickConfig AutoPickConfig { get; set; } = new();

    /// <summary>
    ///     自动剧情配置
    /// </summary>
    public AutoSkipConfig AutoSkipConfig { get; set; } = new();

    /// <summary>
    ///     自动钓鱼配置
    /// </summary>
    public AutoFishingConfig AutoFishingConfig { get; set; } = new();

    /// <summary>
    ///     自动钓鱼配置
    /// </summary>
    public QuickTeleportConfig QuickTeleportConfig { get; set; } = new();

    /// <summary>
    ///     自动打牌配置
    /// </summary>
    public AutoGeniusInvokationConfig AutoGeniusInvokationConfig { get; set; } = new();

    /// <summary>
    ///     自动伐木配置
    /// </summary>
    public AutoWoodConfig AutoWoodConfig { get; set; } = new();

    /// <summary>
    ///     自动战斗配置
    /// </summary>
    public AutoFightConfig AutoFightConfig { get; set; } = new();

    /// <summary>
    ///     自动秘境配置
    /// </summary>
    public AutoDomainConfig AutoDomainConfig { get; set; } = new();

    /// <summary>
    ///     脚本类配置
    /// </summary>
    public MacroConfig MacroConfig { get; set; } = new();

    /// <summary>
    ///     快捷键配置
    /// </summary>
    public HotKeyConfig HotKeyConfig { get; set; } = new();

    /// <summary>
    ///     通知配置
    /// </summary>
    public NotificationConfig NotificationConfig { get; set; } = new();

    [JsonIgnore]
    public Action? OnAnyChangedAction { get; set; }

    public void InitEvent()
    {
        PropertyChanged += OnAnyPropertyChanged;
        MaskWindowConfig.PropertyChanged += OnAnyPropertyChanged;
        CommonConfig.PropertyChanged += OnAnyPropertyChanged;
        GenshinStartConfig.PropertyChanged += OnAnyPropertyChanged;
        NotificationConfig.PropertyChanged += OnAnyPropertyChanged;
        NotificationConfig.PropertyChanged += OnNotificationPropertyChanged;

        AutoPickConfig.PropertyChanged += OnAnyPropertyChanged;
        AutoSkipConfig.PropertyChanged += OnAnyPropertyChanged;
        AutoFishingConfig.PropertyChanged += OnAnyPropertyChanged;
        QuickTeleportConfig.PropertyChanged += OnAnyPropertyChanged;
        MacroConfig.PropertyChanged += OnAnyPropertyChanged;
        HotKeyConfig.PropertyChanged += OnAnyPropertyChanged;
        AutoWoodConfig.PropertyChanged += OnAnyPropertyChanged;
        AutoFightConfig.PropertyChanged += OnAnyPropertyChanged;
        AutoDomainConfig.PropertyChanged += OnAnyPropertyChanged;
    }

    public void OnAnyPropertyChanged(object? sender, EventArgs args)
    {
        GameTaskManager.RefreshTriggerConfigs();
        OnAnyChangedAction?.Invoke();
    }

    public void OnNotificationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        NotificationService.Instance().RefreshNotifiers();
    }
}
