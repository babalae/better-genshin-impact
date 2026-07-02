namespace BetterGenshinImpact.Core.CloudGame;

/// <summary>
/// 云原神父窗口的显示模式。
/// 显示模式只改变父 HWND 的可见性与可交互性，不改变 WebView2 控件自身的 Visibility。
/// </summary>
public enum CloudGameDisplayMode
{
    /// <summary>
    /// 显示窗口并允许用户直接操作。
    /// </summary>
    Interactive,

    /// <summary>
    /// 显示窗口但禁用父窗口，只用于观察画面。
    /// </summary>
    ReadOnly,

    /// <summary>
    /// 隐藏父窗口，WebView2 Controller 仍保持创建状态。
    /// </summary>
    Hidden
}
