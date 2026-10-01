using BetterGenshinImpact.Core.Input.Backends.WebSdk;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using Fischless.GameCapture;
using Fischless.GameCapture.Graphics;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.GameTask.Runtime.WebPage;

/// <summary>
/// 云原神网页版的运行环境：打开（或复用）宿主窗口，等待进入游戏后，组装窗口、WGC 截图和 WebSdk 输入。
/// <para>
/// 宿主窗口由本类持有，<see cref="GameRuntime.Dispose"/> 不会关闭它（与"停止截图器不关闭本地游戏"一致）。
/// </para>
/// <para>
/// 网页版实例没有主界面，宿主窗口就是唯一界面：它被设为 Application.MainWindow，关闭即退出实例。
/// </para>
/// </summary>
public sealed class WebPageRuntimeProvider(
    Func<CloudWebHostWindow> hostFactory,
    IConfigService configService,
    ILogger<WebPageRuntimeProvider> logger) : IGameRuntimeProvider
{
    private CloudWebHostWindow? _host;

    public GameRuntimeKind Kind => GameRuntimeKind.WebPage;

    /// <summary>
    /// 关闭宿主窗口前停止运行环境，由 GameRuntimeService 设置。
    /// </summary>
    public Func<Task>? StopCaptureBeforeCloseAsync { private get; set; }

    public async Task<GameRuntime?> AcquireAsync(CancellationToken ct)
    {
        var host = EnsureHost();
        if (!host.IsGameReady)
        {
            logger.LogInformation("等待云原神就绪：请在云原神窗口中完成登录、排队并进入游戏");
        }

        if (!await host.WaitGameReadyAsync(ct))
        {
            logger.LogInformation("云原神窗口已关闭，取消启动截图器");
            return null;
        }

        var window = new WebPageGameWindow(host);
        IGameCapture? capture = null;
        try
        {
            // 最小化时 WGC 拿不到帧，画面尺寸也不合规
            window.Activate();

            // 固定使用 WGC（GraphicsCapture），不读截图模式配置：config.json 由所有实例共享，网页版改了会影响 Primary。
            // 与 Win32 路径一样在 UI 线程上创建（帧池用 Direct3D11CaptureFramePool.Create）
            capture = new GraphicsCapture();
            capture.Start(host.Handle, GameCaptureSettings.From(configService.Get()));

            // 输入坐标与截图使用同一块画面区域
            var input = new WebSdkInputBackend(host.Bridge!, () => window.Viewport.ScreenRect);
            return new GameRuntime(Kind, window, capture, input);
        }
        catch
        {
            capture?.Dispose();
            window.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 关闭宿主窗口，即退出网页版实例（见 <see cref="OnHostClosed"/>）
    /// </summary>
    public void CloseGame()
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            _host?.Close();
        }
        else
        {
            dispatcher.Invoke(() => _host?.Close());
        }
    }

    /// <summary>
    /// 已打开的宿主直接复用（最小化时还原），否则新建。在 UI 线程上调用
    /// </summary>
    private CloudWebHostWindow EnsureHost()
    {
        if (_host is { IsClosed: false } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            return existing;
        }

        var host = hostFactory();
        host.StopCaptureBeforeCloseAsync = StopCaptureBeforeCloseAsync;
        host.Closed += OnHostClosed;
        _host = host;

        // 没有主界面：宿主作为主窗口，供对话框 Owner、UIDispatcherHelper.MainWindow 等使用
        Application.Current.MainWindow = host;
        host.Show();
        return host;
    }

    /// <summary>
    /// 宿主是网页版实例唯一的界面，关闭后退出实例（CloseGame 同样会走到这里）
    /// </summary>
    private void OnHostClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_host, sender))
        {
            _host = null;
        }

        var app = Application.Current;
        if (app != null && !app.Dispatcher.HasShutdownStarted)
        {
            logger.LogInformation("云原神窗口已关闭，退出网页版实例");
            app.Dispatcher.BeginInvoke(new Action(app.Shutdown));
        }
    }
}
