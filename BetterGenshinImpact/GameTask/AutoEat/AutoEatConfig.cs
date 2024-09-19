using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoEat;

    /// <summary>
    ///自动吃加血药配置
    /// </summary>
    [Serializable]
public partial class AutoEatConfig : ObservableObject
{
    /// <summary>
    /// 触发器是否启用
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;

    /// <summary>
    /// 触发器触发间隔
    /// </summary>
    [ObservableProperty]
    private int _intervalMs = 500;
}

