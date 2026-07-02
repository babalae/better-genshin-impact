using BetterGenshinImpact.GameTask.Session;
using System;
using System.Drawing;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.CloudGame;

/// <summary>
/// 云原神运行目标，实现任务系统所需的通用游戏目标抽象。
/// 该类型只负责目标窗口生命周期及显示模式，截图和输入分别由独立组件提供。
/// </summary>
/// <param name="hostWindow">当前会话独占的 WebView2 窗口宿主。</param>
public sealed class CloudGameTarget(CloudGameHostWindow hostWindow) : IGameTarget
{
    /// <summary>
    /// 获取 WebView2 宿主线程及窗口是否仍存活。
    /// </summary>
    public bool IsAlive => hostWindow.IsAlive;

    /// <summary>
    /// 云原神可以在父窗口非前台时继续运行。
    /// </summary>
    public bool CanRunInBackground => true;

    /// <summary>
    /// 获取承载 WebView2 的父 HWND。
    /// </summary>
    public IntPtr WindowHandle => hostWindow.ParentHandle;

    /// <summary>
    /// 获取任务系统使用的固定 1920×1080 画面尺寸。
    /// </summary>
    public Size CaptureSize => new(1920, 1080);

    /// <summary>
    /// 获取最近一次成功应用的父窗口显示模式。
    /// </summary>
    public CloudGameDisplayMode DisplayMode { get; private set; } = CloudGameDisplayMode.Interactive;

    /// <summary>
    /// 显示并启用父窗口，将控制权交给用户。
    /// </summary>
    public async Task ShowInteractiveAsync()
    {
        // 必须先完成 HWND 操作，再更新状态，避免 UI 展示与真实窗口状态不一致。
        await hostWindow.ShowInteractiveAsync();
        DisplayMode = CloudGameDisplayMode.Interactive;
    }

    /// <summary>
    /// 显示但禁用父窗口，避免用户输入与自动任务输入相互干扰。
    /// </summary>
    public async Task ShowReadOnlyAsync()
    {
        await hostWindow.ShowReadOnlyAsync();
        DisplayMode = CloudGameDisplayMode.ReadOnly;
    }

    /// <summary>
    /// 仅隐藏父 HWND，不销毁或隐藏 WebView2 控件。
    /// </summary>
    public async Task HideAsync()
    {
        await hostWindow.HideAsync();
        DisplayMode = CloudGameDisplayMode.Hidden;
    }

    /// <summary>
    /// 关闭当前目标独占的 WebView2 宿主窗口和 STA Dispatcher。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return hostWindow.DisposeAsync();
    }
}
