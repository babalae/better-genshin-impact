using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 主题类型配置
/// </summary>
public enum ThemeType
{
    DarkNone,
    DarkMica,
    DarkAcrylic,
    LightNone,
    LightMica,
    LightAcrylic,
}


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
    private bool _screenshotUidCoverEnabled = true;

    /// <summary>
    ///     退出时最小化至托盘
    /// </summary>
    [ObservableProperty]
    private bool _exitToTray;

    /// <summary>
    /// 当前主题类型（新版主题）
    /// </summary>
    [ObservableProperty]
    private ThemeType _currentThemeType = OsVersionHelper.IsWindows11_22523_OrGreater ? ThemeType.DarkMica : ThemeType.DarkNone;

    /// <summary>
    /// 主题（旧版主题，兼容性保留）
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
    
    
    /// <summary>
    /// 当前看过的兑换码推送版本
    /// </summary>
    [ObservableProperty]
    private string _redeemCodeFeedsUpdateVersion = "20251013";

    /// <summary>
    /// 备份时是否包含脚本文件
    /// </summary>
    [ObservableProperty]
    private bool _backupIncludeScripts = true;

    /// <summary>
    /// 是否启用远程备份
    /// </summary>
    [ObservableProperty]
    private bool _remoteBackupEnabled = false;

    /// <summary>
    /// 远程备份类型：RemoteFolder, OSS, WebDAV
    /// </summary>
    [ObservableProperty]
    private string _remoteBackupType = "RemoteFolder";

    /// <summary>
    /// 远程文件夹路径（UNC路径或映射的网络驱动器）
    /// </summary>
    [ObservableProperty]
    private string _remoteBackupFolderPath = string.Empty;

    /// <summary>
    /// OSS Endpoint
    /// </summary>
    [ObservableProperty]
    private string _ossEndpoint = string.Empty;

    /// <summary>
    /// OSS Access Key ID
    /// </summary>
    [ObservableProperty]
    private string _ossAccessKeyId = string.Empty;

    /// <summary>
    /// OSS Access Key Secret
    /// </summary>
    [ObservableProperty]
    private string _ossAccessKeySecret = string.Empty;

    /// <summary>
    /// OSS Bucket Name
    /// </summary>
    [ObservableProperty]
    private string _ossBucketName = string.Empty;

    /// <summary>
    /// OSS 存储路径前缀
    /// </summary>
    [ObservableProperty]
    private string _ossPathPrefix = "BetterGI/Backups/";

    /// <summary>
    /// WebDAV 服务器地址
    /// </summary>
    [ObservableProperty]
    private string _webDavUrl = string.Empty;

    /// <summary>
    /// WebDAV 用户名
    /// </summary>
    [ObservableProperty]
    private string _webDavUsername = string.Empty;

    /// <summary>
    /// WebDAV 密码
    /// </summary>
    [ObservableProperty]
    private string _webDavPassword = string.Empty;

    /// <summary>
    /// WebDAV 远程路径
    /// </summary>
    [ObservableProperty]
    private string _webDavRemotePath = "/BetterGI/Backups/";
}
