using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 原神启动配置
/// </summary>
[Serializable]
public partial class GenshinStartConfig : ObservableObject
{

    /// <summary>
    /// 联动启动原神本体
    /// </summary>
    [ObservableProperty] private bool _linkedStartEnabled = true;

    /// <summary>
    /// 原神安装路径
    /// </summary>
    [ObservableProperty] private string _installPath = "";

    /// <summary>
    /// 原神启动参数
    /// </summary>
    [ObservableProperty] private string _genshinStartArgs = "";
}