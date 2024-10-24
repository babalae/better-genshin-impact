using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingConfig : ObservableObject
{
    // 主要行走追踪的角色编号
    [ObservableProperty]
    private string _mainAvatarIndex = string.Empty;

    // [盾角]使用元素战技的角色编号
    [ObservableProperty]
    private string _guardianAvatarIndex = string.Empty;

    // [盾角]使用元素战技的时间间隔(ms)
    [ObservableProperty]
    private int _guardianElementalSkillInterval = 10000;

    // [盾角]使用元素战技的方式 长按/短按
    [ObservableProperty]
    private bool _guardianElementalSkillLongPress = false;

    // normal_attack 配置几号位
    [ObservableProperty]
    private string _normalAttackAvatarIndex = string.Empty;

    // elemental_skill 配置几号位
    [ObservableProperty]
    private string _elementalSkillAvatarIndex = string.Empty;
}
