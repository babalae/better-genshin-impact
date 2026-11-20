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
    /// 连接桌面分身时是否使用已保存的密码自动登录。默认开启：没存密码时只尝试 RDP
    /// 记住的上一次凭据，失败或没有就退回手动输入。密码存放在 Windows 凭据管理器。
    /// </summary>
    [ObservableProperty]
    private bool _autoLoginEnabled = true;

    /// <summary>
    /// 全屏时浮层工具栏是否固定显示（不随鼠标离开自动收起）。
    ///
    /// 与窗口置顶无关：这是工具栏的显示策略，用户在大屏上可能需要它一直挂着。
    /// </summary>
    [ObservableProperty]
    private bool _fullscreenBarPinned = false;
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
