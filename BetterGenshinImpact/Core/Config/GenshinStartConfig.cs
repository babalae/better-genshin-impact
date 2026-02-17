using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     原神启动配置
/// </summary>
[Serializable]
public partial class GenshinStartConfig : ObservableObject
{
    // /// <summary>
    // ///     自动点击月卡
    // /// </summary>
    // [ObservableProperty]
    // private bool _autoClickBlessingOfTheWelkinMoonEnabled;

    /// <summary>
    ///     自动进入游戏（开门）
    /// </summary>
    [ObservableProperty]
    private bool _autoEnterGameEnabled = true;

    /// <summary>
    ///     原神启动参数
    /// </summary>
    [ObservableProperty]
    private string _genshinStartArgs = "";

    /// <summary>
    ///     原神安装路径
    /// </summary>
    [ObservableProperty]
    private string _installPath = "";

    /// <summary>
    ///     联动启动原神本体
    /// </summary>
    [ObservableProperty]
    private bool _linkedStartEnabled = true;

    /// <summary>
    ///     使用Starward同步记录时间
    /// </summary>
    [ObservableProperty]
    private bool _recordGameTimeEnabled = false;

    [ObservableProperty]
    private bool _startGameWithCmd = false;
}
