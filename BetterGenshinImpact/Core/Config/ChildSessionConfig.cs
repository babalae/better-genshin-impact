using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class ChildSessionConfig : ObservableObject
{
    /// <summary>
    /// 桌面分身普通窗口模式的位置（物理像素坐标）。
    /// </summary>
    [ObservableProperty]
    private WindowPositionConfig? _normalWindowPosition;

    /// <summary>
    /// 桌面分身小窗模式的位置（物理像素坐标）。
    /// </summary>
    [ObservableProperty]
    private WindowPositionConfig? _smallWindowPosition;

    /// <summary>
    /// 桌面分身窗口是否置顶。
    /// </summary>
    [ObservableProperty]
    private bool _topmostEnabled = false;

    /// <summary>
    /// RDP 画面是否自适应窗口。关闭时使用 1:1 显示。
    /// </summary>
    [ObservableProperty]
    private bool _smartSizingEnabled = true;

    /// <summary>
    /// 桌面分身窗口是否保持 16:9 宽高比。
    /// </summary>
    [ObservableProperty]
    private bool _keepAspectRatio = true;

    /// <summary>
    /// Alt+Tab、Windows 键等系统组合键是否发送到桌面分身。
    /// </summary>
    [ObservableProperty]
    private bool _sendSystemShortcutsToRemote = true;

    /// <summary>
    /// 桌面分身是否启用游戏鼠标模式。默认使用普通鼠标模式。
    /// </summary>
    [ObservableProperty]
    private bool _gameMouseModeEnabled = false;

    /// <summary>
    /// 桌面分身的 RDP 音频是否静音，不影响主桌面的其他程序。
    /// </summary>
    [ObservableProperty]
    private bool _audioMuted = false;

    /// <summary>
    /// 桌面分身是否处于观看模式。开启后键鼠不会在桌面分身内生效，仅显示画面。
    /// </summary>
    [ObservableProperty]
    private bool _watchModeEnabled = false;

    /// <summary>
    /// 连接桌面分身时是否自动填写登录凭据。
    /// </summary>
    [ObservableProperty]
    private bool _autoLoginEnabled = false;

    /// <summary>
    /// 全屏时浮层工具栏是否固定显示（不随鼠标离开自动收起）。
    ///
    /// 与窗口置顶无关：这是工具栏的显示策略，用户在大屏上可能需要它一直挂着。
    /// </summary>
    [ObservableProperty]
    private bool _fullscreenBarPinned = false;

    /// <summary>
    /// 各账户已保存的密码（键为账户名，值为 DPAPI 加密后的 Base64 密文）。
    ///
    /// 桌面分身只能用当前 Windows 账户登录，所以每次只会用到「当前账户」那一条；
    /// 但一台机器上可能换着账户用，按账户分开存，换回来时不用重新输一遍。
    /// </summary>
    public Dictionary<string, string> AutoLoginPasswords { get; set; } = new();
}

[Serializable]
public sealed class WindowPositionConfig
{
    public int Left { get; set; }

    public int Top { get; set; }

    public WindowPositionConfig()
    {
    }

    public WindowPositionConfig(int left, int top)
    {
        Left = left;
        Top = top;
    }
}
