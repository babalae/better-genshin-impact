using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     遮罩窗口配置
/// </summary>
[Serializable]
public partial class CommonConfig : ObservableObject
{
    /// <summary>
    ///     是否启用遮罩窗口
    /// </summary>
    [ObservableProperty]
    private bool _screenshotEnabled;

    /// <summary>
    ///     UID遮盖是否启用
    /// </summary>
    [ObservableProperty]
    private bool _screenshotUidCoverEnabled;

    /// <summary>
    ///     退出时最小化至托盘
    /// </summary>
    [ObservableProperty]
    private bool _exitToTray;
    
    /// <summary>
    /// 主题
    /// </summary>
    [ObservableProperty]
    private WindowBackdropType _currentBackdropType = WindowBackdropType.Mica;
    
    /// <summary>
    /// 是否是第一次运行
    /// </summary>
    [ObservableProperty]
    private bool _isFirstRun = true;
    
    /// <summary>
    /// 这个版本是否运行过
    /// </summary>
    [ObservableProperty]
    private string _runForVersion = string.Empty;
    
    /// <summary>
    /// 一个设备只运行一次的已运行设备ID列表
    /// </summary>
    [ObservableProperty]
    private List<string> _onceHadRunDeviceIdList = new();
}
