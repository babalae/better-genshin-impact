using BetterGenshinImpact.Core.Input.Backends.WebSdk;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 云原神网页版宿主窗口：承载页面、注入输入 SDK、轮询 SDK 状态判断游戏是否就绪。
/// <para>
/// 网页版实例没有主界面，这个窗口就是唯一的界面。由 WebPageRuntimeProvider 创建并持有，关闭即退出实例。
/// </para>
/// <para>
/// 客户区固定为 1920x1080 物理像素（不随 DPI 缩放），截图与识图直接工作在 1080P 下。
/// 页面也固定按 100% 渲染，不跟随系统缩放（见 <see cref="ConfigureController"/>）。
/// WPF 的 WebView2 控件不公开 CoreWebView2Controller、无法关闭系统缩放，所以这里直接在窗口句柄上托管 Controller。
/// </para>
/// <para>
/// 注意：RTC 断开（断网、被踢下线等）后只会让 <see cref="IsGameReady"/> 变为 false，截图器随之停止；
/// 这里不会自动刷新页面，也不会自动重新绑定。实例没有主界面，需要关闭本窗口后从主实例重新启动。
/// </para>
/// </summary>
public partial class CloudWebHostWindow : Window
{
    /// <summary>
    /// 客户区尺寸（物理像素），锁定为 1080P
    /// </summary>
    private const int ClientWidth = 1920;
    private const int ClientHeight = 1080;

    /// <summary>
    /// 已就绪后连续多少次查询未就绪才判定为断开，避免单次查询失败就停掉任务
    /// </summary>
    private const int DisconnectThreshold = 3;

    /// <summary>
    /// 禁用 Chromium 的遮挡与后台降频，否则会影响自动化：
    /// 前两项让窗口被遮挡时照常渲染（否则 WGC 一直拿到旧帧）；
    /// 后两项让页面在后台不降优先级、不限流定时器（SDK 的 tapKey / click 用 setTimeout 控制按住时长）。
    /// 每个实例的用户数据目录独立，这些参数不会与 HtmlMask 等功能的 WebView2 环境冲突
    /// </summary>
    private const string BrowserArguments =
        "--disable-features=CalculateNativeWinOcclusion " +
        "--disable-backgrounding-occluded-windows " +
        "--disable-renderer-backgrounding " +
        "--disable-background-timer-throttling";

    /// <summary>
    /// 云原神网页版地址。autobegin=1 让页面加载后自动开始游戏（进入排队），不需要手动点"开始游戏"
    /// </summary>
    private const string CloudGameUrl = "https://ys.mihoyo.com/cloud/?autobegin=1";

    private readonly string _instanceName;
    private readonly ILogger<CloudWebHostWindow> _logger;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// 直接托管在本窗口句柄上的 WebView2，页面初始化完成后可用，随窗口关闭释放。只在 UI 线程访问
    /// </summary>
    private CoreWebView2Controller? _controller;

    private volatile bool _isGameReady;
    private volatile bool _isClosed;
    private bool _stoppingCaptureForClose;
    private bool _captureStoppedForClose;
    private bool _polling;
    private int _notReadyCount;
    private string? _lastNotReadyReason;

    public CloudWebHostWindow(InstanceBootstrap bootstrap, ILogger<CloudWebHostWindow> logger)
    {
        _instanceName = bootstrap.Context.InstanceName
                        ?? throw new InvalidOperationException("只有网页版实例可以打开云原神宿主窗口");
        _logger = logger;

        InitializeComponent();
        Title = $"云原神 · {_instanceName}";

        _statusTimer.Tick += OnStatusTimerTick;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateControllerBounds();
        // 页面里的下拉框、输入法候选框等需要知道窗口移动了
        LocationChanged += (_, _) => _controller?.NotifyParentWindowPositionChanged();
        // 窗口激活时把键盘焦点交给页面，否则用户登录时输入不进去
        Activated += (_, _) => _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>
    /// 窗口句柄，SourceInitialized 之后可用
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>
    /// 输入 SDK 调用桥，页面初始化完成后可用。随窗口关闭释放
    /// </summary>
    public WebView2InputBridge? Bridge { get; private set; }

    /// <summary>
    /// 关闭窗口前停止 WGC 和截图调度，由运行环境提供。
    /// </summary>
    public Func<Task>? StopCaptureBeforeCloseAsync { get; set; }

    /// <summary>
    /// RTC 数据通道为 open 且游戏数据已开始。任意线程可读。
    /// 断开后不会自动恢复绑定，见类注释
    /// </summary>
    public bool IsGameReady => _isGameReady && !_isClosed;

    public bool IsClosed => _isClosed;

    /// <summary>
    /// 等待游戏就绪（登录、排队可能需要几分钟，不设超时）。
    /// 就绪返回 true；窗口被关闭返回 false；取消时抛出 <see cref="OperationCanceledException"/>
    /// </summary>
    public async Task<bool> WaitGameReadyAsync(CancellationToken ct)
    {
        while (!_isClosed)
        {
            if (IsGameReady)
            {
                return true;
            }

            await Task.Delay(500, ct);
        }

        return false;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Handle = new WindowInteropHelper(this).Handle;
        ApplyClientSize(center: true);
    }

    /// <summary>
    /// 换到 DPI 不同的显示器时 WPF 会按 DIP 缩放窗口，这里重新按物理像素锁定客户区
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplyClientSize(center: false));
    }

    /// <summary>
    /// 按物理像素把客户区设为 1920x1080：外框尺寸 = 当前边框 + 目标客户区，直接用 SetWindowPos 设置，避免 DIP 换算的舍入误差
    /// </summary>
    private void ApplyClientSize(bool center)
    {
        if (Handle == 0 || WindowState != WindowState.Normal
                        || !User32.GetWindowRect(Handle, out var windowRect)
                        || !User32.GetClientRect(Handle, out var clientRect))
        {
            return;
        }

        var width = windowRect.Width - clientRect.Width + ClientWidth;
        var height = windowRect.Height - clientRect.Height + ClientHeight;
        var flags = User32.SetWindowPosFlags.SWP_NOZORDER | User32.SetWindowPosFlags.SWP_NOACTIVATE;
        int x = 0, y = 0;
        if (center)
        {
            // 进程为 Per-Monitor DPI 感知，WinForms Screen 返回物理像素
            var workArea = System.Windows.Forms.Screen.FromHandle(Handle).WorkingArea;
            if (width > workArea.Width || height > workArea.Height)
            {
                _logger.LogWarning("当前显示器工作区 {W}x{H} 小于云原神窗口 {WW}x{WH}，窗口会超出屏幕", workArea.Width,
                    workArea.Height, width, height);
            }

            x = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
            y = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        }
        else
        {
            if (clientRect.Width == ClientWidth && clientRect.Height == ClientHeight)
            {
                return;
            }

            flags |= User32.SetWindowPosFlags.SWP_NOMOVE;
        }

        _ = User32.SetWindowPos(Handle, HWND.NULL, x, y, width, height, flags);
    }

    /// <summary>
    /// WebView 铺满客户区（物理像素）。最小化时客户区为空，保持原尺寸
    /// </summary>
    private void UpdateControllerBounds()
    {
        if (_controller == null || !User32.GetClientRect(Handle, out var clientRect)
                                || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return;
        }

        _controller.Bounds = new System.Drawing.Rectangle(0, 0, clientRect.Width, clientRect.Height);
    }

    /// <summary>
    /// 页面必须按 1:1 铺满 1920x1080 客户区，并且在任何系统缩放下布局都一致：
    /// <list type="bullet">
    /// <item>不跟随系统缩放：WebView2 默认按显示器 DPI 设置 RasterizationScale（150% 时页面视口只有 1280x720 CSS 像素，
    /// 页面自己的悬浮菜单、登录框等都会跟着放大）。这里关闭检测并固定为 1，页面视口始终是 1920x1080 CSS 像素、devicePixelRatio 为 1；</item>
    /// <item>Bounds 按物理像素给出，与客户区一致；</item>
    /// <item>禁止 Ctrl + 滚轮 / Ctrl + +/- 缩放并固定为 100%，关闭左下角的链接状态栏（会被截进画面）；</item>
    /// <item>开发者工具只在 Debug 下可用；输入桥依赖 WebMessage，显式开启。</item>
    /// </list>
    /// </summary>
    private void ConfigureController(CoreWebView2Controller controller)
    {
        controller.ShouldDetectMonitorScaleChanges = false;
        controller.RasterizationScale = 1d;
        controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
        controller.DefaultBackgroundColor = System.Drawing.Color.Black;
        controller.ZoomFactor = 1d;
        controller.IsVisible = true;

        var settings = controller.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = RuntimeHelper.IsDebug;
        settings.IsWebMessageEnabled = true;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;

        _controller = controller;
        UpdateControllerBounds();
        if (IsActive)
        {
            controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions(BrowserArguments);
            var environment = await CoreWebView2Environment.CreateAsync(
                null, WebViewInstanceStore.GetDataFolder(_instanceName), options);
            if (_isClosed) return;

            var controller = await environment.CreateCoreWebView2ControllerAsync(Handle);
            if (_isClosed)
            {
                controller.Close();
                return;
            }

            ConfigureController(controller);

            Bridge = await WebView2InputBridge.InstallAsync(controller.CoreWebView2);
            if (_isClosed) return;

            controller.CoreWebView2.Navigate(CloudGameUrl);
            _statusTimer.Start();
            _logger.LogInformation("云原神网页版「{Name}」已打开 {Url}，请在窗口中登录并进入游戏", _instanceName, CloudGameUrl);
        }
        catch (Exception ex) when (_isClosed)
        {
            // 初始化过程中窗口被关闭，WebView2 会以 E_ABORT 结束
            _logger.LogDebug(ex, "云原神宿主窗口已关闭，WebView2 初始化已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "云原神宿主窗口初始化失败");
            await ThemedMessageBox.ErrorAsync($"云原神页面初始化失败：{ex.Message}\n请确认已安装 Microsoft Edge WebView2 Runtime。");
            Close();
        }
    }

    private async void OnStatusTimerTick(object? sender, EventArgs e)
    {
        if (_polling || Bridge == null || _isClosed)
        {
            return;
        }

        _polling = true;
        try
        {
            var status = await Bridge.GetStatusAsync();
            UpdateReadyState(status);
        }
        finally
        {
            _polling = false;
        }
    }

    private void UpdateReadyState(WebSdkStatus status)
    {
        if (status.IsReady)
        {
            _notReadyCount = 0;
            _lastNotReadyReason = null;
            if (!_isGameReady)
            {
                _isGameReady = true;
                _logger.LogInformation("云原神网页版「{Name}」已进入游戏", _instanceName);
            }

            return;
        }

        if (_isGameReady)
        {
            if (++_notReadyCount < DisconnectThreshold)
            {
                return;
            }

            // 只标记断开，不自动刷新页面、不自动重新绑定（见类注释）
            _isGameReady = false;
            _logger.LogWarning("云原神网页版「{Name}」连接已断开（RTC：{State}），截图器将停止",
                _instanceName, status.RtcDataChannelState ?? status.Error ?? "unknown");
        }

        // 未就绪原因只在变化时记录，避免每秒刷日志（例如页面更新导致 ClientCore 模块找不到）
        var reason = status.Error ?? $"rtc={status.RtcDataChannelState ?? "null"}, gameDataStarted={status.GameDataStarted?.ToString() ?? "null"}";
        if (reason != _lastNotReadyReason)
        {
            _lastNotReadyReason = reason;
            _logger.LogDebug("云原神网页版尚未就绪：{Reason}", reason);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_captureStoppedForClose && StopCaptureBeforeCloseAsync is { } stopCapture)
        {
            e.Cancel = true;
            if (_stoppingCaptureForClose)
            {
                return;
            }

            _stoppingCaptureForClose = true;
            _isClosed = true; // 让等待游戏就绪的启动流程退出，StopAsync 才能取得启动锁
            _statusTimer.Stop();
            _isGameReady = false;
            _ = StopCaptureAndCloseAsync(stopCapture);
            return;
        }

        _statusTimer.Stop();
        _isGameReady = false;
    }

    private async Task StopCaptureAndCloseAsync(Func<Task> stopCapture)
    {
        try
        {
            await stopCapture();
            _captureStoppedForClose = true;
            // 避免 StopAsync 同步完成时在 Closing 事件内部重入 Close。
            Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭云原神窗口前停止截图器失败");
            _captureStoppedForClose = false;
            _stoppingCaptureForClose = false;
            _isClosed = false;
            _statusTimer.Start();
            await ThemedMessageBox.ErrorAsync($"停止截图器失败：{ex.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _statusTimer.Tick -= OnStatusTimerTick;
        Bridge?.Dispose();
        _controller?.Close();
        _controller = null;
    }
}
