using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingPartyConfig : ObservableObject
{
    // 配置是否启用，不启用会使用路径追踪内的条件配置
    [ObservableProperty]
    private bool _enabled = false;

    // 切换到队伍的名称
    [ObservableProperty]
    private string _partyName = string.Empty;

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
    
    // 使用小道具的间隔时间
    [ObservableProperty]
    private int _useGadgetIntervalMs = 0;

    // 启用进入剧情自动脱离
    [ObservableProperty]
    private bool _autoSkipEnabled = true;

    //启用自动战斗配置
    [ObservableProperty]
    private bool _autoFightEabled = false;

    [ObservableProperty]
    private AutoFightConfig _autoFightConfig = new();

    public static PathingPartyConfig BuildDefault()
    {
        // 即便是不启用的情况下也设置默认值，减少后续使用的判断
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        return new PathingPartyConfig
        {
            OnlyInTeleportRecover = pathingConditionConfig.OnlyInTeleportRecover,
            UseGadgetIntervalMs = pathingConditionConfig.UseGadgetIntervalMs
        };
    }
}
