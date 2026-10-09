namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 游戏运行环境的种类，按游戏画面的载体划分。由实例类型决定，进程内不变
/// </summary>
public enum GameRuntimeKind
{
    /// <summary>
    /// 本机 Win32 游戏窗口：本地原神、Windows 云原神
    /// </summary>
    Win32Window,

    /// <summary>
    /// WebView2 承载的云原神网页版
    /// </summary>
    WebPage,
}
