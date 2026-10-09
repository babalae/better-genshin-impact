using System;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 游戏画面所在的顶层窗口，网页版为宿主窗口。
/// <para>
/// 成员都是实时查询，会在截图调度的线程池线程上调用，实现不能访问 WPF 对象。
/// </para>
/// </summary>
public interface IGameWindow : IDisposable
{
    /// <summary>
    /// 顶层窗口句柄。用于截图、遮罩定位，兼容 TaskContext.GameHandle
    /// </summary>
    nint Handle { get; }

    /// <summary>
    /// 窗口所属进程。网页版为当前 BetterGI 进程
    /// </summary>
    int ProcessId { get; }

    GameViewport Viewport { get; }

    /// <summary>
    /// 游戏是否仍在运行。Win32：游戏进程未退出；网页版：宿主窗口未关闭且 RTC 通道为 open
    /// </summary>
    bool IsAlive { get; }

    /// <summary>
    /// 前台窗口就是 <see cref="Handle"/>
    /// </summary>
    bool IsForeground { get; }

    bool IsMinimized { get; }

    /// <summary>
    /// 操作游戏是否依赖前台。
    /// Win32 为 true（SendInput 只发往前台窗口）；网页版为 false（输入经 RTC 发往云端），失焦后任务继续运行
    /// </summary>
    bool RequiresForeground { get; }

    /// <summary>
    /// 让窗口进入可操作状态。Win32：还原并置前；网页版：只从最小化还原，不抢前台
    /// </summary>
    void Activate();

    /// <summary>
    /// 窗口移动或缩放。只做通知，最新值以 <see cref="Viewport"/> 为准
    /// </summary>
    event EventHandler? ViewportChanged;
}
