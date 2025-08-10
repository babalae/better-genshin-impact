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
    private int _checkInterval = 500;

    /// <summary>
    /// 吃药间隔时间（毫秒）
    /// 防止频繁吃药
    /// </summary>
    [ObservableProperty]
    private int _eatInterval = 2000;
}