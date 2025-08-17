using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoEat;

/// <summary>
/// 自动吃药配置
/// </summary>
[Serializable]
public partial class AutoEatConfig : ObservableObject
{
    /// <summary>
    /// 是否启用自动吃药
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;

    /// <summary>
    /// 是否显示吃药通知
    /// </summary>
    [ObservableProperty]
    private bool _showNotification = true;

    /// <summary>
    /// 检测间隔时间（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _checkInterval = 150;

    /// <summary>
    /// 吃药间隔时间（毫秒）
    /// 防止频繁吃药
    /// </summary>
    [ObservableProperty]
    private int _eatInterval = 1000;

    /// <summary>
    /// 测试食物名称
    /// </summary>
    [ObservableProperty]
    private string? _testFoodName;

    /// <summary>
    /// 默认的攻击类料理名称
    /// </summary>
    [ObservableProperty]
    private string? _defaultAtkBoostingDishName;

    /// <summary>
    /// 默认的冒险类料理名称
    /// </summary>
    [ObservableProperty]
    private string? _defaultAdventurersDishName;

    /// <summary>
    /// 默认的防御类料理名称
    /// </summary>
    [ObservableProperty]
    private string? _defaultDefBoostingDishName;
}