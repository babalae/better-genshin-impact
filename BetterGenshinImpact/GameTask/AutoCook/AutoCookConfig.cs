using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoCook;

/// <summary>
///自动烹饪配置
/// </summary>
[Serializable]
public partial class AutoCookConfig : ObservableObject
{
    /// <summary>
    /// 触发器是否启用
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;
}
