using System;
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
