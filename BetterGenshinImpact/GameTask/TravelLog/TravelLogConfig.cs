using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.TravelLog;

/// <summary>
/// 移动轨迹记录配置
/// </summary>
[Serializable]
public partial class TravelLogConfig : ObservableObject
{
    /// <summary>
    /// 是否启用移动轨迹记录
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;
}
