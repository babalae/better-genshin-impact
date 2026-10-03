using System;
using System.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口宿主：唯一负责遮罩窗口的创建、显隐、置顶和跟随游戏窗口。
/// View 层以外的代码只能通过它接触遮罩窗口。
/// </summary>
public interface IMaskWindowHost
{
    /// <summary>
    /// UI 线程调用。首次调用时创建窗口，绑定游戏窗口并显示
    /// </summary>
    void Attach(nint gameHandle);

    /// <summary>
    /// UI 线程调用。隐藏窗口并解除绑定，窗口实例保留复用
    /// </summary>
    void Detach();

    /// <summary>
    /// UI 线程调用。关闭并释放窗口（程序退出时）
    /// </summary>
    void Close();

    /// <summary>
    /// 任意线程调用，每帧可调，立即返回。宿主去重后决定显隐、置顶、跟随
    /// </summary>
    void ReportGameWindow(GameWindowState state);

    /// <summary>
    /// 最近一次应用到窗口上的状态，任意线程可读
    /// </summary>
    MaskWindowState State { get; }

    /// <summary>
    /// 可见性或位置变化后在 UI 线程触发，附属遮罩（HtmlMask 等）订阅
    /// </summary>
    event EventHandler<MaskWindowState>? StateChanged;
}

/// <summary>
/// 游戏窗口的状态。描述的是游戏窗口，不是遮罩窗口，所以不加 MaskWindow 前缀
/// </summary>
/// <param name="IsCapturing">截图器是否在运行</param>
/// <param name="IsActive">游戏窗口是否在前台</param>
/// <param name="IsMinimized">游戏窗口是否最小化</param>
/// <param name="IsForegroundOwnedByBetterGiOrGame">前台窗口属于 BetterGI 自身或游戏进程（或者没有前台窗口）。按进程 ID 判断</param>
/// <param name="Bounds">游戏捕获区域（屏幕物理像素），为空表示本次不更新位置</param>
public readonly record struct GameWindowState(
    bool IsCapturing,
    bool IsActive,
    bool IsMinimized,
    bool IsForegroundOwnedByBetterGiOrGame,
    RECT Bounds);

/// <summary>
/// 遮罩窗口的状态
/// </summary>
/// <param name="IsVisible">是否可见</param>
/// <param name="Bounds">窗口位置（DIP）</param>
public readonly record struct MaskWindowState(bool IsVisible, Rect Bounds);
