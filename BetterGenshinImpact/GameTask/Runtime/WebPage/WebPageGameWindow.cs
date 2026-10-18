using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.Runtime.Win32;
using BetterGenshinImpact.View.Windows;
using System.ComponentModel;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Runtime.WebPage;

/// <summary>
/// 云原神网页版的游戏窗口，即宿主窗口。
/// <para>
/// 宿主是真实的顶层 Win32 窗口，且 WebView 铺满客户区，所以几何、前台、最小化和窗口移动监听都直接复用 Win32 实现，
/// 覆写存活判断、输入坐标缩放、是否依赖前台和激活方式。必须在 UI 线程创建和释放。
/// </para>
/// </summary>
public sealed class WebPageGameWindow : Win32GameWindow
{
    private readonly CloudWebHostWindow _host;

    public WebPageGameWindow(CloudWebHostWindow host) : base(host.Handle)
    {
        _host = host;
        _host.Closing += OnHostClosing;
    }

    /// <summary>
    /// 宿主窗口未关闭，且 RTC 通道为 open、已进入游戏。
    /// 注意：断开后截图器会停止，但不会自动刷新页面或重新绑定。网页版实例没有主界面、不响应热键，
    /// 无法手动重新启动截图器，需要关闭云原神窗口（即退出实例）后从主实例重新启动
    /// </summary>
    public override bool IsAlive => _host.IsGameReady;

    /// <summary>
    /// WebView2 页面固定以 100% 渲染，云端输入坐标不随宿主显示器的 DPI 缩放。
    /// </summary>
    public override GameViewport Viewport => new(SystemControl.GetCaptureRect(Handle), 1f);

    /// <summary>
    /// 输入经 RTC 发往云端，不需要前台：失焦后任务继续运行
    /// </summary>
    public override bool RequiresForeground => false;

    /// <summary>
    /// 只从最小化还原，不抢前台。最小化后截图器拿不到新帧
    /// </summary>
    public override void Activate()
    {
        if (IsMinimized)
        {
            User32.ShowWindow(Handle, ShowWindowCommand.SW_RESTORE);
        }
    }

    /// <summary>
    /// 页面销毁前释放所有按下的按键（UI 线程上直接投递到页面）
    /// </summary>
    private void OnHostClosing(object? sender, CancelEventArgs e) => InputHub.ReleaseAll();

    protected override void Dispose(bool disposing)
    {
        _host.Closing -= OnHostClosing;
        base.Dispose(disposing);
    }
}
