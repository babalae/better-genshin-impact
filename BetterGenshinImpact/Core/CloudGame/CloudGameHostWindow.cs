using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using BetterGenshinImpact.Core.Config;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BetterGenshinImpact.Core.CloudGame;

/// <summary>
/// 在独立 STA 线程中承载云原神 WebView2 的窗口宿主。
/// 每个实例拥有独立的父 HWND 和 WebView2 用户数据目录，以隔离登录态及浏览器运行状态。
/// 隐藏运行只通过 Win32 父窗口控制，不添加 Chromium 后台渲染参数。
/// </summary>
public sealed class CloudGameHostWindow : IAsyncDisposable
{
    // Win32 ShowWindow 命令：隐藏父窗口。
    private const int SwHide = 0;

    // Win32 ShowWindow 命令：显示并允许窗口被激活。
    private const int SwShow = 5;

    // Win32 ShowWindow 命令：显示窗口但不抢占当前前台焦点。
    private const int SwShowNoActivate = 8;

    // 实例显示名称，同时用于窗口标题和 STA 线程名称。
    private readonly string _name;

    // 当前 Profile 独占的 WebView2 用户数据目录。
    private readonly string _userDataFolder;

    // WebView2 初始化完成后导航的云原神地址。
    private readonly string _url;

    // 窗口关闭完成信号，供跨线程关闭流程等待。
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 当前实例独占的 STA 线程。
    private Thread? _thread;

    // STA 线程对应的 WPF Dispatcher，所有 WebView2 操作必须通过它执行。
    private Dispatcher? _dispatcher;

    // 只用于承载 WebView2 的无边框 WPF 父窗口。
    private Window? _window;

    // 当前实例独占的 WebView2 控件。
    private WebView2? _webView;

    // WPF 窗口完成 SourceInitialized 后得到的原生父 HWND。
    private IntPtr _parentHandle;

    // WebView2 BrowserProcessId，用于填充现有 ISystemInfo 进程信息。
    private int _browserProcessId;

    /// <summary>
    /// 创建云原神 WebView2 宿主的配置对象，实际窗口由 <see cref="StartAsync"/> 创建。
    /// </summary>
    /// <param name="name">实例显示名称。</param>
    /// <param name="userDataFolder">当前 Profile 独占的 WebView2 UDF。</param>
    /// <param name="url">需要导航的云原神页面地址。</param>
    public CloudGameHostWindow(string name, string userDataFolder, string url)
    {
        _name = name;
        _userDataFolder = userDataFolder;
        _url = url;
    }

    /// <summary>
    /// 获取承载 WebView2 的父 HWND；SourceInitialized 前为零。
    /// </summary>
    public IntPtr ParentHandle => _parentHandle;

    /// <summary>
    /// 获取 WebView2 浏览器进程 ID；初始化完成前为零。
    /// </summary>
    public int BrowserProcessId => _browserProcessId;

    /// <summary>
    /// 获取 CoreWebView2 是否已经创建完成。
    /// </summary>
    public bool IsInitialized => _webView?.CoreWebView2 != null;

    /// <summary>
    /// 获取 STA 线程和宿主窗口是否仍处于存活状态。
    /// </summary>
    public bool IsAlive => _thread?.IsAlive == true && !_closed.Task.IsCompleted;

    /// <summary>
    /// 页面开始导航时触发，用于清空不可跨页面重放的输入命令。
    /// </summary>
    public event EventHandler? NavigationStarted;

    /// <summary>
    /// 页面完成导航时触发。
    /// </summary>
    public event EventHandler? NavigationCompleted;

    /// <summary>
    /// WebView2 任一浏览器进程异常退出时触发。
    /// </summary>
    public event EventHandler<CoreWebView2ProcessFailedEventArgs>? ProcessFailed;

    /// <summary>
    /// 创建专用 STA 线程、WPF 父窗口及 WebView2 实例。
    /// 返回时 WebView2 已完成初始化并开始导航。
    /// </summary>
    public Task StartAsync()
    {
        if (_thread != null)
        {
            throw new InvalidOperationException("云原神 WebView2 已经启动");
        }

        // completion 只表示初始化完成；窗口最终关闭由 _closed 单独表示。
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // 每个实例独占线程，确保 WPF Window、Dispatcher 和 WebView2 的线程亲和性。
        _thread = new Thread(() => RunWindowThread(completion))
        {
            IsBackground = true,
            Name = $"CloudGame-{_name}"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// 显示并启用父 HWND，同时将窗口切到前台。
    /// </summary>
    public Task ShowInteractiveAsync() => SetDisplayModeAsync(CloudGameDisplayMode.Interactive);

    /// <summary>
    /// 禁用父 HWND 并以不激活方式显示窗口。
    /// </summary>
    public Task ShowReadOnlyAsync() => SetDisplayModeAsync(CloudGameDisplayMode.ReadOnly);

    /// <summary>
    /// 隐藏父 HWND，不改变 WebView2 控件的 WPF Visibility。
    /// </summary>
    public Task HideAsync() => SetDisplayModeAsync(CloudGameDisplayMode.Hidden);

    /// <summary>
    /// 使用 WebView2 的 CapturePreview API 捕获当前页面。
    /// 所有调用都会切换到 WebView2 所属的 STA Dispatcher。
    /// </summary>
    public async Task<MemoryStream> CapturePreviewAsync(CancellationToken cancellationToken = default)
    {
        // WebView2 具有线程亲和性，调用方线程不能直接访问控件。
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("云原神窗口尚未初始化");
        return await dispatcher.InvokeAsync(async () =>
        {
            // 在 STA 回调内再次确认控件仍未被关闭。
            var webView = _webView ?? throw new InvalidOperationException("云原神 WebView2 尚未初始化");

            // 每次捕获使用独立内存流，所有权随返回值交给截图帧泵。
            var stream = new MemoryStream();
            await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);

            // CapturePreview 写完后流位于末尾，解码前必须重置位置。
            stream.Position = 0;
            return stream;
        }, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }

    /// <summary>
    /// 在页面主世界执行脚本。调用会被调度到 WebView2 所属线程。
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        // ExecuteScriptAsync 也必须在创建 WebView2 的 STA 线程执行。
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("云原神窗口尚未初始化");
        return await dispatcher.InvokeAsync(async () =>
        {
            // 在 STA 回调内再次确认控件仍未被关闭。
            var webView = _webView ?? throw new InvalidOperationException("云原神 WebView2 尚未初始化");
            return await webView.CoreWebView2.ExecuteScriptAsync(script);
        }, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }

    /// <summary>
    /// 请求 STA 线程关闭窗口，并等待 Closed 事件完成全部清理。
    /// </summary>
    public Task CloseAsync()
    {
        // Dispatcher 为空表示线程尚未启动或无需关闭。
        var dispatcher = _dispatcher;
        if (dispatcher == null)
        {
            return Task.CompletedTask;
        }

        // 使用 BeginInvoke 避免调用线程与 Dispatcher 互相等待造成死锁。
        dispatcher.BeginInvoke(() => _window?.Close());
        return _closed.Task;
    }

    /// <summary>
    /// STA 线程入口，创建窗口和 WebView2 控件后启动 WPF 消息循环。
    /// </summary>
    /// <param name="completion">WebView2 初始化成功或失败的完成信号。</param>
    private void RunWindowThread(TaskCompletionSource completion)
    {
        // 显式安装 DispatcherSynchronizationContext，保证 async 事件返回当前 STA。
        _dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_dispatcher));

        // WebView2 与父窗口必须在同一 STA 线程创建。
        _webView = new WebView2();
        _window = new Window
        {
            Title = $"BetterGI - {_name}",
            Width = 1920,
            Height = 1080,
            MinWidth = 960,
            MinHeight = 540,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.Black,
            Content = _webView
        };

        _window.SourceInitialized += (_, _) =>
        {
            // 只有 SourceInitialized 后 WPF Window 才拥有可供 Win32 控制的 HWND。
            _parentHandle = new WindowInteropHelper(_window).Handle;

            // WPF 尺寸使用 DIP；按当前显示器 DPI 反算，保证实际像素仍为 1920×1080。
            var source = PresentationSource.FromVisual(_window);

            // 获取 DIP 到设备像素的转换矩阵；无法取得时按 100% DPI 处理。
            var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            _window.Width = 1920d / transform.M11;
            _window.Height = 1080d / transform.M22;
        };
        _window.Closed += (_, _) =>
        {
            try
            {
                // WebView2 必须在创建它的 STA 线程释放。
                _webView?.Dispose();
            }
            finally
            {
                // 先唤醒所有等待关闭的调用方，再退出 Dispatcher 消息循环。
                _closed.TrySetResult();
                _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        };
        _window.Loaded += async (_, _) =>
        {
            try
            {
                // 等窗口和 HWND 均就绪后再创建 CoreWebView2。
                await InitializeWebViewAsync();
                completion.TrySetResult();
            }
            catch (Exception e)
            {
                // 将初始化错误传回调用方，并关闭已经创建的空窗口。
                completion.TrySetException(e);
                _window.Close();
            }
        };

        // 首次启动默认以交互模式显示，便于用户完成登录。
        _window.Show();

        // 保持专用 STA 线程消息泵，直到窗口 Closed 请求关闭 Dispatcher。
        Dispatcher.Run();
    }

    /// <summary>
    /// 初始化 WebView2 设置、输入脚本及稳定包装层，然后导航到云原神页面。
    /// 注入脚本必须在文档创建前注册，确保页面 webpack Runtime 初始化后可以立即被发现。
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        if (_webView == null)
        {
            throw new InvalidOperationException("云原神 WebView2 创建失败");
        }

        // UDF 按 Profile 隔离，Cookie、LocalStorage 和登录态不会跨实例共享。
        Directory.CreateDirectory(_userDataFolder);

        // 不传 BrowserExecutableFolder 和附加参数，使用系统 WebView2 Runtime 默认配置。
        var environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);

        // 保存浏览器进程 ID 供会话兼容现有进程相关 API。
        _browserProcessId = (int)_webView.CoreWebView2.BrowserProcessId;

        // 关闭自动化场景不需要的用户功能，避免右键菜单等界面干扰任务。
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.NavigationStarting += (_, _) =>
        {
            NavigationStarted?.Invoke(this, EventArgs.Empty);
        };
        _webView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            NavigationCompleted?.Invoke(this, EventArgs.Empty);
        };
        _webView.CoreWebView2.ProcessFailed += (_, e) => ProcessFailed?.Invoke(this, e);
        _webView.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            // 登录流程请求新窗口时复用当前实例，避免创建未受会话管理的浏览器窗口。
            e.Handled = true;
            _webView.CoreWebView2.Navigate(e.Uri);
        };

        // 先读取附件脚本，再与稳定包装层一起注册为“文档创建时执行”。
        var injectionPath = Global.Absolute(@"Assets\CloudGame\ys-input-inject.js");

        // 保持附件脚本文本原样读取，不在 C# 中拼接或改写内容。
        var injection = await File.ReadAllTextAsync(injectionPath);
        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(injection);
        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CloudGameJsBridge.DispatcherScript);

        // 所有注入均注册完成后才允许页面导航。
        _webView.CoreWebView2.Navigate(_url);
    }

    /// <summary>
    /// 通过父 HWND 切换显示状态，不改变 WebView2 的 WPF Visibility。
    /// </summary>
    private Task SetDisplayModeAsync(CloudGameDisplayMode mode)
    {
        // 父窗口操作同样回到窗口所属 Dispatcher，避免 WPF 状态与 HWND 状态竞争。
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("云原神窗口尚未初始化");
        return dispatcher.InvokeAsync(() =>
        {
            if (_parentHandle == IntPtr.Zero)
            {
                return;
            }

            switch (mode)
            {
                case CloudGameDisplayMode.Interactive:
                    // 交互模式：启用、显示并主动切到前台。
                    EnableWindow(_parentHandle, true);
                    ShowWindow(_parentHandle, SwShow);
                    SetForegroundWindow(_parentHandle);
                    break;
                case CloudGameDisplayMode.ReadOnly:
                    // 只读模式：禁用父窗口并显示，但不抢占其他窗口焦点。
                    EnableWindow(_parentHandle, false);
                    ShowWindow(_parentHandle, SwShowNoActivate);
                    break;
                case CloudGameDisplayMode.Hidden:
                    // 隐藏模式：只隐藏父 HWND，不能设置 WebView2.Visibility 或销毁 Controller。
                    EnableWindow(_parentHandle, false);
                    ShowWindow(_parentHandle, SwHide);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }).Task;
    }

    /// <summary>
    /// 异步释放宿主，等价于关闭窗口并等待 STA 清理完成。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }

    /// <summary>
    /// 启用或禁用父窗口的用户输入。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool enable);

    /// <summary>
    /// 直接控制父 HWND 显示状态，绕过 WPF Visibility。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    /// <summary>
    /// 将交互模式窗口设置为前台窗口。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
