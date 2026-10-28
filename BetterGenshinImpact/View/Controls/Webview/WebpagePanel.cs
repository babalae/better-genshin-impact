using BetterGenshinImpact.Infrastructure.WebView;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.View.Controls.Webview;

/// <summary>
/// 软件内网页面板：更新日志、脚本仓库、地图路径编辑器、日志分析。
/// 使用 <see cref="WebViewProfiles.Default"/>，经 <see cref="WebViewSetup"/> 初始化；导航请求会等到初始化完成后执行
/// </summary>
public class WebpagePanel : UserControl
{
    private readonly ILogger<WebpagePanel> _logger = App.GetLogger<WebpagePanel>();
    private Uri _currentUri = null!;
    private WebView2 _webView = null!;

    /// <summary>
    /// 承载 WebView 和异常提示的容器；WebView 是原生窗口，提示不能叠在它上面，只能替换显示
    /// </summary>
    private readonly Grid _host = new();

    /// <summary>
    /// 网页进程异常退出后的提示，替换 WebView 显示
    /// </summary>
    private FrameworkElement? _crashNotice;

    /// <summary>
    /// 初始化任务，第一次加载或导航时开始，只执行一次
    /// </summary>
    private Task? _initialization;

    public WebView2 WebView => _webView;

    /// <summary>
    /// 页面下载的保存目录，需在第一次导航前设置；为空时拒绝页面发起的下载
    /// </summary>
    public string? DownloadFolderPath { get; set; }

    /// <summary>
    /// 初始化完成后调用，用于添加虚拟主机映射、HostObject 等特有逻辑；在首次导航之前执行
    /// </summary>
    public Action? OnWebViewInitializedAction { get; set; }
    
    public Action<CoreWebView2NavigationCompletedEventArgs>? OnNavigationCompletedAction { get; set; }

    public WebpagePanel()
    {
        if (!WebViewRuntime.IsAvailable)
        {
            Content = CreateDownloadButton();
        }
        else
        {
            // 不设置 CreationProperties：环境由 WebViewSetup 从环境入口取得，数据目录统一在 Data/WebView2/App
            _webView = new WebView2();
            _webView.NavigationStarting += NavigationStarting_CancelNavigation;
            _webView.NavigationCompleted += WebView_NavigationCompleted;

            // TODO: change the theme from `md2html.html` to fit it firstly.
            // AdditionalBrowserArguments = "--enable-features=WebContentsForceDark"
            _host.Children.Add(_webView);
            Content = _host;
            Loaded += (_, _) => _ = EnsureInitializedAsync();
        }
    }
    
    // public WebpagePanel(WebView2 webView2)
    // {
    //     if (!IsWebView2Available())
    //     {
    //         Content = CreateDownloadButton();
    //     }
    //     else
    //     {
    //         EnsureWebView2DataFolder();
    //         _webView = webView2;
    //         webView2.CreationProperties = new CoreWebView2CreationProperties
    //         {
    //             UserDataFolder = Path.Combine(new FileInfo(Environment.ProcessPath!).DirectoryName!, @"WebView2Data\\"),
    //         };
    //         webView2.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
    //         webView2.NavigationStarting += NavigationStarting_CancelNavigation;
    //         Content = webView2;
    //     }
    // }
    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // 调用外部设置的导航完成 Action
        OnNavigationCompletedAction?.Invoke(e);
    }

    /// <summary>
    /// 开始初始化（只执行一次）并返回初始化任务；运行时不可用时为已完成的任务
    /// </summary>
    private Task EnsureInitializedAsync() => _webView == null ? Task.CompletedTask : _initialization ??= InitializeAsync();

    /// <summary>
    /// 用环境入口初始化控件，再执行外部设置的初始化 Action
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            var provider = App.GetService<IWebViewEnvironmentProvider>()
                           ?? throw new InvalidOperationException("WebView2 环境入口尚未初始化");
            await _webView.InitializeAsync(provider, WebViewProfiles.Default, new WebViewOptions
            {
                DownloadFolder = DownloadFolderPath,
                ProcessCrashed = ShowCrashNotice,
            });
            // 调用外部设置的 Action
            OnWebViewInitializedAction?.Invoke();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "软件内网页初始化失败");
            ShowNotice($"网页初始化失败：{e.Message}", null);
        }
    }

    /// <summary>
    /// 初始化完成后在界面线程执行导航；可以从任意线程调用
    /// </summary>
    private void RunWhenReady(Action navigate)
    {
        if (_webView == null)
            return;

        Dispatcher.InvokeAsync(async () =>
        {
            await EnsureInitializedAsync();
            if (_webView?.CoreWebView2 != null)
            {
                navigate();
            }
        });
    }

    public static bool IsWebView2Available() => WebViewRuntime.IsAvailable;

    public static Uri FilePathToFileUrl(string filePath)
    {
        var uri = new StringBuilder();
        foreach (var v in filePath)
            if (v >= 'a' && v <= 'z' || v >= 'A' && v <= 'Z' || v >= '0' && v <= '9' ||
                v == '+' || v == '/' || v == ':' || v == '.' || v == '-' || v == '_' || v == '~' ||
                v > '\x80')
                uri.Append(v);
            else if (v == Path.DirectorySeparatorChar || v == Path.AltDirectorySeparatorChar)
                uri.Append('/');
            else
                uri.Append($"%{(int)v:X2}");
        if (uri.Length >= 2 && uri[0] == '/' && uri[1] == '/') // UNC path
            uri.Insert(0, "file:");
        else
            uri.Insert(0, "file:///");

        try
        {
            return new Uri(uri.ToString());
        }
        catch
        {
            return null!;
        }
    }

    public void NavigateToFile(string path)
    {
        var uri = Path.IsPathRooted(path) ? FilePathToFileUrl(path) : new Uri(path);

        NavigateToUri(uri);
    }

    public void NavigateToUri(Uri uri)
    {
        if (_webView == null)
            return;

        // 先记录目标地址，导航开始时据此放行
        _currentUri = uri;
        RunWhenReady(() => _webView.Source = uri);
    }

    public void NavigateToHtml(string htmlContentOrUrl)
    {
        // 检查是否是URL（以file:或http开头）
        if (htmlContentOrUrl.StartsWith("file:") || htmlContentOrUrl.StartsWith("http"))
        {
            // 如果是URL，使用Navigate方法
            _currentUri = new Uri(htmlContentOrUrl);
            RunWhenReady(() => _webView.CoreWebView2.Navigate(htmlContentOrUrl));
        }
        else
        {
            // 如果是HTML内容，使用NavigateToString方法
            RunWhenReady(() => _webView.NavigateToString(htmlContentOrUrl));
        }
    }

    public void NavigateToMd(string md, string backgroundColor = "#2b2b2b")
    {
        md = WebUtility.HtmlEncode(md);
        string md2html = File.ReadAllText(Global.Absolute(@"Assets\Strings\md2html.html"), Encoding.UTF8);
        var html = md2html.Replace("{{content}}", md).Replace("#202020", backgroundColor);
        NavigateToHtml(html);
    }

    private void NavigationStarting_CancelNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:")) // when using NavigateToString
            return;
            
        // 允许file:和http:开头的URL导航（用于大型HTML内容）
        if (e.Uri.StartsWith("file:") || e.Uri.StartsWith("http:") || e.Uri.StartsWith("https:"))
        {
            if (_currentUri != null && e.Uri == _currentUri.ToString())
                return;
        }

        var newUri = new Uri(e.Uri);
        if (newUri != _currentUri) e.Cancel = true;
    }

    /// <summary>
    /// 网页进程反复异常退出时替换显示说明；渲染进程退出时可以重新加载，浏览器进程退出后只能重新打开窗口
    /// </summary>
    private void ShowCrashNotice(CoreWebView2ProcessFailedKind kind)
    {
        if (kind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            ShowNotice("网页进程已退出，请关闭此窗口后重新打开。", null);
            return;
        }

        ShowNotice("网页进程异常退出。", () =>
        {
            HideNotice();
            _webView?.CoreWebView2?.Reload();
        });
    }

    /// <summary>
    /// 隐藏 WebView，显示一段说明和可选的“重新加载”按钮
    /// </summary>
    private void ShowNotice(string message, Action? reload)
    {
        HideNotice();
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        if (reload != null)
        {
            var button = new Button { Content = "重新加载", HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 4, 16, 4) };
            button.Click += (_, _) => reload();
            panel.Children.Add(button);
        }

        _crashNotice = panel;
        if (_webView != null)
        {
            _webView.Visibility = Visibility.Collapsed;
        }

        _host.Children.Add(panel);
    }

    /// <summary>
    /// 移除说明，恢复显示 WebView
    /// </summary>
    private void HideNotice()
    {
        if (_crashNotice != null)
        {
            _host.Children.Remove(_crashNotice);
            _crashNotice = null;
        }

        if (_webView != null)
        {
            _webView.Visibility = Visibility.Visible;
        }
    }

    public void Dispose()
    {
        _webView?.Dispose();
        _webView = null!;
    }

    /// <summary>
    /// 运行时缺失时的说明和下载入口
    /// </summary>
    private Button CreateDownloadButton()
    {
        var button = new Button
        {
            Content = "查看需要安装 Microsoft Edge WebView2 点击这里开始下载",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(20, 6, 20, 6)
        };
        // 需要 UseShellExecute 才能用系统浏览器打开链接
        button.Click += (sender, e) => WebViewRuntimePrompt.OpenDownloadPage();

        return button;
    }
}
