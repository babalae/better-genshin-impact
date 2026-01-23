using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 自动吃药配置
/// </summary>
[Serializable]
public partial class MapMaskConfig : ObservableObject
{
    /// <summary>
    /// 是否启用
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;
}