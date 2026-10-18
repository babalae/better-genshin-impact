namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 可整体切换的输入后端
/// </summary>
public enum InputBackendKind
{
    /// <summary>
    /// Windows 键鼠：前台 SendInput + 后台 PostMessage
    /// </summary>
    Win32,

    /// <summary>
    /// 云原神网页版 JS SDK
    /// </summary>
    WebSdk,

    /// <summary>
    /// 手柄（预留）
    /// </summary>
    Gamepad,
}

/// <summary>
/// 输入层内部使用的鼠标按键。
/// 不复用 WPF 的 System.Windows.Input.MouseButton 与 Fischless 的 MouseButton，避免重名
/// </summary>
public enum InputMouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}
