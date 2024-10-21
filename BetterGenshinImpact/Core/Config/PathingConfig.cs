using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingConfig : ObservableObject
{
    // 主要行走追踪的角色编号
    [ObservableProperty]
    private int _mainAvatarIndex = 1;

    // [盾角]使用元素战技的角色编号
    [ObservableProperty]
    private int _guardianAvatarIndex = 1;

    // [盾角]使用元素战技的时间间隔(ms)
    [ObservableProperty]
    private int _guardianElementalSkillInterval = 0;

    // [盾角]使用元素战技的方式 长按/短按
    [ObservableProperty]
    private bool _guardianElementalSkillLongPress = false;

    // 纳西妲处于几号位置 nahida_collect 配置
    [ObservableProperty]
    private int _nahidaAvatarIndex = 1;

    // normal_attack 配置几号位
    [ObservableProperty]
    private int _normalAttackAvatarIndex = 1;

    // elemental_skill 配置几号位
    [ObservableProperty]
    private int _elementalSkillAvatarIndex = 1;
}
