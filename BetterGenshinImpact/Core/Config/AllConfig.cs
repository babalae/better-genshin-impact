using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.QuickTeleport;
using BetterGenshinImpact.Service.Notification;
using CommunityToolkit.Mvvm.ComponentModel;
using Fischless.GameCapture;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.InventoryMaterialStats;
using BetterGenshinImpact.GameTask.AutoEat;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoCook;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.SkillCd;
using BetterGenshinImpact.GameTask.UseRedeemCode;

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

    /// <summary>
    ///     触发器触发频率(ms)
    /// </summary>
    [ObservableProperty]
    private int _triggerInterval = 50;

    /// <summary>
    ///     WGC V2 帧率上限（毫秒，即最小更新间隔，限制 DWM 推帧频率以降低 GPU 占用）
    ///     0 = 不启用限流（默认）；仅 Windows 11 24H2 及以上系统生效
    /// </summary>
    [ObservableProperty]
    private int _wgcMinUpdateIntervalMs;

    /// <summary>
    ///     WGC V2 使用 CPU 颜色转换（BGRA→BGR 由 CPU CvtColor 完成）
    ///     默认关闭 = GPU compute shader 打包 BGR24（回读量更小、CPU 零转换）；重启捕获后生效
    /// </summary>
    [ObservableProperty]
    private bool _wgcV2UseCpuConvert;

    // /// <summary>
    // ///     WGC使用位图缓存
    // ///     高帧率情况下，可能会导致卡顿
    // ///     云原神可能会出现黑屏
    // /// </summary>
    // [ObservableProperty]
    // private bool _wgcUseBitmapCache = true;

    /// <summary>
    /// 自动修复Win11下BitBlt截图方式不可用的问题
    /// </summary>
    [ObservableProperty]
    private bool _autoFixWin11BitBlt = true;

    // /// <summary>
    // /// 推理使用的设备
    // /// </summary>
    // [ObservableProperty]
    // private string _inferenceDevice = "CPU";

    [ObservableProperty]
    private List<ValueTuple<string, int, string, string>> _nextScheduledTask = [];
    
    /// <summary>
    /// 禁用键鼠监听，需重启
    /// </summary>
    [ObservableProperty]
    private bool _disableInputMonitor = false;

    /// <summary>
    /// 连续执行任务时，从此任务开始执行
    /// </summary>
    [JsonIgnore]
    public string NextScriptGroupName { get; set; }= string.Empty;
    
    /// <summary>
    /// 一条龙选中使用的配置
    /// </summary>
    [ObservableProperty]
    private string _selectedOneDragonFlowConfigName = string.Empty;

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
    ///     自动连招配置
    /// </summary>
    public AutoComboBuildConfig AutoComboBuildConfig { get; set; } = new();

    /// <summary>
    ///     快速传送配置
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
    ///     自动乐曲配置 - 千音雅集
    /// </summary>
    public AutoMusicGameConfig AutoMusicGameConfig { get; set; } = new();

    /// <summary>
    ///     自动秘境配置
    /// </summary>
    public AutoDomainConfig AutoDomainConfig { get; set; } = new();

    /// <summary>
    ///     自动首领讨伐配置
    /// </summary>
    public AutoBossConfig AutoBossConfig { get; set; } = new();
    
    
    /// <summary>
    ///     自动秘境配置
    /// </summary>
    public AutoStygianOnslaughtConfig AutoStygianOnslaughtConfig { get; set; } = new();

    /// <summary>
    ///     自动分解圣遗物配置
    /// </summary>
    public AutoArtifactSalvageConfig AutoArtifactSalvageConfig { get; set; } = new();

    /// <summary>
    ///     自动吃药配置
    /// </summary>
    public AutoEatConfig AutoEatConfig { get; set; } = new();

    /// <summary>
    ///     自动地脉花配置
    /// </summary>
    public AutoLeyLineOutcropConfig AutoLeyLineOutcropConfig { get; set; } = new();

    public AutoCookConfig AutoCookConfig { get; set; } = new();
    
    /// <summary>
    ///   地图遮罩
    /// </summary>
    public MapMaskConfig MapMaskConfig { get; set; } = new();

    /// <summary>
    /// 技能 CD 提示
    /// </summary>
    public SkillCdConfig SkillCdConfig { get; set; } = new();

    /// <summary>
    /// 自动使用
    /// </summary>
    public AutoRedeemCodeConfig AutoRedeemCodeConfig { get; set; } = new();

    /// <summary>
    ///     截取物品图标配置
    /// </summary>
    public GetGridIconsConfig GetGridIconsConfig { get; set; } = new();

    /// <summary>
    ///     背包材料统计
    /// </summary>
    public InventoryMaterialStatsConfig InventoryMaterialStatsConfig { get; set; } = new();

    /// <summary>
    ///     宏配置
    /// </summary>
    public MacroConfig MacroConfig { get; set; } = new();

    public RecordConfig RecordConfig { get; set; } = new();

    /// <summary>
    /// 原琴演奏配置
    /// </summary>
    public MusicConfig MusicConfig { get; set; } = new();

    /// <summary>
    /// 脚本配置
    /// </summary>
    public ScriptConfig ScriptConfig { get; set; } = new();

    /// <summary>
    /// 地图追踪配置
    /// </summary>
    public PathingConditionConfig PathingConditionConfig { get; set; } = PathingConditionConfig.Default;

    /// <summary>
    ///     快捷键配置
    /// </summary>
    public HotKeyConfig HotKeyConfig { get; set; } = new();

    /// <summary>
    ///     通知配置
    /// </summary>
    public NotificationConfig NotificationConfig { get; set; } = new();

    /// <summary>
    /// 原神按键绑定配置
    /// </summary>
    public KeyBindingsConfig KeyBindingsConfig { get; set; } = new();

    /// <summary>
    /// 其他配置
    /// </summary>
    public OtherConfig OtherConfig { get; set; } = new();

    /// <summary>
    /// 传送相关配置
    /// </summary>
    public TpConfig TpConfig { get; set; } = new();

    /// <summary>
    /// 开发者配置
    /// </summary>
    public DevConfig DevConfig { get; set; } = new();


    /// <summary>
    /// 硬件加速设置
    /// </summary>
    public HardwareAccelerationConfig HardwareAccelerationConfig { get; set; } = new();

    /// <summary>
    /// 桌面分身配置
    /// </summary>
    public ChildSessionConfig ChildSessionConfig { get; set; } = new();

    /// <summary>
    /// 任意配置项变更后的回调（由 ConfigService 设置为防抖保存）
    /// </summary>
    [JsonIgnore]
    public Action? OnAnyChangedAction { get; set; }

    private ConfigChangeTracker? _changeTracker;

    /// <summary>
    /// 开始追踪整个配置对象图的变更（含嵌套对象与集合），重复调用无副作用
    /// </summary>
    public void InitEvent()
    {
        if (_changeTracker != null)
        {
            return;
        }

        NotificationConfig.PropertyChanged += OnNotificationPropertyChanged;
        _changeTracker = new ConfigChangeTracker(OnConfigChanged);
        _changeTracker.Track(this);
    }

    /// <param name="sender">触发变更的对象（某个子配置、嵌套对象或集合）</param>
    private void OnConfigChanged(object sender)
    {
        // 实时触发器每帧读取配置，不需要在这里通知；只有切换遮罩显示相关的开关时清掉旧的识别结果
        if (sender is MaskWindowConfig)
        {
            TaskContext.Instance().Runtime?.MaskWindowDrawingBoard.ClearAll();
        }

        OnAnyChangedAction?.Invoke();
    }

    public void OnNotificationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        NotificationService.Instance().RefreshNotifiers();
    }
}
