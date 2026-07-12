using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoEat;
using BetterGenshinImpact.GameTask.AutoFight;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Config;

public enum RecoverTiming
{
    AnyWaypoint,
    OnlyTeleport,
    Never
}

/// <summary>
/// 从旧字段 OnlyInTeleportRecover 迁移到 RecoverTiming 枚举的共享方法
/// </summary>
internal static class RecoverTimingMigration
{
    public static RecoverTiming Migrate(bool onlyInTeleportRecover)
        => onlyInTeleportRecover ? RecoverTiming.OnlyTeleport : RecoverTiming.AnyWaypoint;
}

[Serializable]
public partial class PathingPartyConfig : ObservableObject
{
    // 配置是否启用，不启用会使用地图追踪内的条件配置
    [ObservableProperty]
    private bool _enabled = true;
    
    // 是否启用自动拾取
    [ObservableProperty]
    private bool _autoPickEnabled = true;
    // 切换到队伍的名称
    [ObservableProperty]
    private string _partyName = string.Empty;

    [JsonIgnore]
    public bool SkipPartySwitch { get; set; }
    
    // 切换队伍前是否前往须弥七天神像
    [ObservableProperty]
    private bool _isVisitStatueBeforeSwitchParty = false;
        
    // 主要行走追踪的角色编号
    [ObservableProperty]
    private string _mainAvatarIndex = string.Empty;

    // [盾角]使用元素战技的角色编号
    [ObservableProperty]
    private string _guardianAvatarIndex = string.Empty;

    // [盾角]使用元素战技的时间间隔(s)
    [ObservableProperty]
    private string _guardianElementalSkillSecondInterval = string.Empty;

    // [盾角]使用元素战技的方式 长按/短按
    [ObservableProperty]
    private bool _guardianElementalSkillLongPress = false;

    // // normal_attack 配置几号位
    // [ObservableProperty]
    // private string _normalAttackAvatarIndex = string.Empty;
    //
    // // elemental_skill 配置几号位
    // [ObservableProperty]
    // private string _elementalSkillAvatarIndex = string.Empty;

    // // hydro_collect 配置几号位
    // [ObservableProperty]
    // private string _hydroCollectAvatarIndex = string.Empty;
    //
    // // electro_collect 配置几号位
    // [ObservableProperty]
    // private string _electroCollectAvatarIndex = string.Empty;
    //
    // // anemo_collect 配置几号位
    // [ObservableProperty]
    // private string _anemoCollectAvatarIndex = string.Empty;

    [JsonIgnore]
    public List<string> AvatarIndexList { get; } = ["", "1", "2", "3", "4"];

    // 只在传送传送点时复活
    [ObservableProperty]
    private bool _onlyInTeleportRecover = false;

    // 低血量回复时机
    private RecoverTiming? _recoverTiming;

    public RecoverTiming RecoverTiming
    {
        get
        {
            if (_recoverTiming is null)
            {
                // 首次读取时从旧字段自动迁移
                _recoverTiming = RecoverTimingMigration.Migrate(_onlyInTeleportRecover);
            }
            return _recoverTiming.Value;
        }
        set => SetProperty(ref _recoverTiming, value);
    }

    //允许在jsScript脚本中使用此地图追踪配置
    [ObservableProperty]
    private bool _jsScriptUseEnabled = true;
    
    //允许在此调度器中（一般在JS脚本中）调用自动战斗任务时，采用此追踪配置里的战斗策略
    [ObservableProperty]
    private bool _soloTaskUseFightEnabled = true;
    
    //不在某时执行
    [ObservableProperty] 
    private string _skipDuring = "";
    
    // 使用小道具的间隔时间
    [ObservableProperty]
    private int _useGadgetIntervalMs = 0;

    // 启用进入剧情自动脱离
    [ObservableProperty]
    private bool _autoSkipEnabled = true;
    
    // 自动冲刺启用
    [ObservableProperty]
    private bool _autoRunEnabled = true;
    
    // 启用自动吃药功能
    [ObservableProperty]
    private bool _autoEatEnabled = false;

    /// <summary>
    /// 自动吃食物配置
    /// 供JS脚本使用
    /// </summary>
    [ObservableProperty]
    private AutoEatConfig _autoEatConfig = new();

    //在连续执行时是否隐藏
    [ObservableProperty]
    private bool _hideOnRepeat = false;
    
    //执行周期配置
    [ObservableProperty]
    private PathingPartyTaskCycleConfig _taskCycleConfig = new();
    
    //任务完成跳过执行配置
    [ObservableProperty]
    private TaskCompletionSkipRuleConfig _taskCompletionSkipRuleConfig = new();
    //优先执行其他配置组
    [ObservableProperty]
    private PreExecutionPriorityConfig _preExecutionPriorityConfig = new();

    //启用自动战斗配置
    [ObservableProperty]
    private bool _autoFightEnabled = true;

    [ObservableProperty]
    private AutoFightConfig _autoFightConfig = new();
    // 赶路通用临界距离（米），节点小于此距离时触发接近/切换模式
    [ObservableProperty]
    private int _distance = 45;

    /// <summary>
    /// 接近停止距离（米），强制小于等于 <see cref="Distance"/>，越界时自动使用 Distance 的值。
    /// </summary>
    [ObservableProperty]
    private int _approachStopDistance = 25;

    partial void OnDistanceChanged(int value)
    {
        if (ApproachStopDistance > value)
        {
            ApproachStopDistance = value;
        }
    }

    partial void OnApproachStopDistanceChanged(int value)
    {
        if (value > Distance)
        {
            _approachStopDistance = Distance;
        }
    }

    [JsonIgnore]
    public List<string> HurryOnAvatarList { get; } = ["","自动","玛薇卡","闲云","桑多涅","恰斯卡","流浪者","伊法","希诺宁"];

    [JsonIgnore]
    public List<string> TravelModeList { get; } = ["精准靠近","连续赶路"];

    [ObservableProperty]
    private string _hurryOnAvatar = "";

    [ObservableProperty]
    private string _travelMode = "精准靠近";

    /// <summary>
    /// 接近节点时切人步行
    /// </summary>
    [ObservableProperty]
    private bool _switchToWalkEnabled = false;

    [ObservableProperty]
    private bool _mwkFlyEnabled = true;

    /// <summary>
    /// 玛薇卡跳飞开关
    /// </summary>
    [ObservableProperty]
    private bool _mwkJumpFlyEnabled = true;

    /// <summary>
    /// 跳飞间隔（秒），闲云使用其1/2值
    /// </summary>
    [ObservableProperty]
    private double _mwkJumpFlyIntervalSeconds = 1;

    public static PathingPartyConfig BuildDefault()
    {
        // 即便是不启用的情况下也设置默认值，减少后续使用的判断
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        return new PathingPartyConfig
        {
            OnlyInTeleportRecover = pathingConditionConfig.OnlyInTeleportRecover,
            RecoverTiming = pathingConditionConfig.RecoverTiming,
            UseGadgetIntervalMs = pathingConditionConfig.UseGadgetIntervalMs,
            AutoEatEnabled = pathingConditionConfig.AutoEatEnabled
        };
    }
}
