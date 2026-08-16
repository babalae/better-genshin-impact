using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.View;

/// <summary>
/// HTML遮罩窗口
/// </summary>
public partial class HtmlMaskWindow : Window
{
    private static readonly ConcurrentDictionary<string, HtmlMaskWindow> _windows = new();
    private const int MaxWindows = 5;
    private const string HtmlMaskProfileName = "HtmlMask";

    private readonly string _id;
    private readonly string _workDir;
    private readonly string _webView2DataPath;
    private readonly string? _virtualHostName;
    private readonly string _pageUrl;
    private bool _navigationCompleted;
    private bool _styleCaptured;
    private int _originalStyle;
    private volatile bool _isClickThrough = true;
    private bool _isClosing;
    private Task? _initializationTask;
    private readonly System.Windows.Media.SolidColorBrush _backgroundBrush = new();

    /// <summary>
    /// 窗口唯一标识
    /// </summary>
    public string MaskId => _id;

    /// <summary>
    /// 当前是否处于点击穿透模式
    /// </summary>
    public bool IsClickThrough => _isClickThrough;

    private HtmlMaskWindow(string url, string? id, string workDir)
    {
        _id = id ?? Guid.NewGuid().ToString("N");
        _workDir = Path.GetFullPath(workDir);
        _webView2DataPath = Path.Combine(AppContext.BaseDirectory, "WebView2Data");

        var scriptName = Path.GetFileName(Path.TrimEndingDirectorySeparator(_workDir));
        var scriptKey = CreateScriptKey(scriptName);

        if (Uri.TryCreate(url, UriKind.Absolute, out var pageUri) && pageUri.IsFile)
        {
            _virtualHostName = $"hm-{scriptKey}.bettergi.local";
            _pageUrl = CreateVirtualPageUrl(pageUri, _workDir, _virtualHostName);
        }
        else
        {
            _pageUrl = url;
        }

        InitializeComponent();
        ClickThroughBorder.Background = _backgroundBrush;
        Loaded += OnLoaded;
        Closing += (_, _) => _isClosing = true;
    }

    private static string CreateScriptKey(string scriptName)
    {
        var normalizedName = scriptName.ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static string CreateVirtualPageUrl(Uri fileUri, string workDir, string virtualHostName)
    {
        var pagePath = Path.GetFullPath(fileUri.LocalPath);
        var relativePath = Path.GetRelativePath(workDir, pagePath);

        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException($"HTML页面路径越界访问: {fileUri}", nameof(fileUri));
        }

        var escapedPath = string.Join('/', relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

        var uriBuilder = new UriBuilder(Uri.UriSchemeHttps, virtualHostName)
        {
            Path = $"/{escapedPath}",
            Query = fileUri.Query.TrimStart('?'),
            Fragment = fileUri.Fragment.TrimStart('#')
        };
        return uriBuilder.Uri.AbsoluteUri;
    }

    #region 静态窗口管理

    /// <summary>
    /// 显示HTML遮罩窗口
    /// </summary>
    public static string Show(string url, string? id, string workDir)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            // 指定ID时先关闭已有窗口
            if (id != null && _windows.TryGetValue(id, out var existing))
            {
                existing.Close();
            }

            if (_windows.Count >= MaxWindows)
            {
                throw new InvalidOperationException($"最多同时打开 {MaxWindows} 个HTML遮罩窗口");
            }

            var window = new HtmlMaskWindow(url, id, workDir);
            string wid = window.MaskId;
            _windows[wid] = window;
            window.Closed += (_, _) =>
            {
                _windows.TryRemove(wid, out _);
                window.DisposeWebView();
            };
            window.Show();
            return wid;
        });
    }

    /// <summary>
    /// 关闭指定窗口
    /// </summary>
    public static bool Close(string id)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            window.Dispatcher.Invoke(() => window.Close());
            return true;
        }
        return false;
    }

    /// <summary>
    /// 重新加载指定窗口。窗口仍在初始化时无需额外操作，初始化完成后会读取最新页面内容。
    /// </summary>
    public static bool Reload(string id)
    {
        if (!_windows.TryGetValue(id, out var window))
        {
            return false;
        }

        return window.Dispatcher.Invoke(window.ReloadCore);
    }

    /// <summary>
    /// 关闭所有窗口
    /// </summary>
    public static void CloseAll()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.Dispatcher.Invoke(() => kvp.Value.Close());
        }
    }

    /// <summary>
    /// 隐藏所有窗口（保留生命，不关闭）
    /// </summary>
    public static void HideAll()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.Dispatcher.Invoke(() => kvp.Value.Hide());
        }
    }

    /// <summary>
    /// 显示所有窗口
    /// </summary>
    public static void ShowAll()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.Dispatcher.Invoke(() =>
            {
                kvp.Value.Show();
                kvp.Value.UpdatePosition();
            });
        }
    }

    /// <summary>
    /// 同步所有窗口位置
    /// </summary>
    public static void UpdateAllPositions()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.UpdatePosition();
        }
    }

    /// <summary>
    /// 获取所有窗口ID
    /// </summary>
    public static string[] GetWindowIds()
    {
        return _windows.Keys.ToArray();
    }

    /// <summary>
    /// 窗口是否存在
    /// </summary>
    public static bool Exists(string id)
    {
        return _windows.ContainsKey(id);
    }

    /// <summary>
    /// 获取窗口实例，不存在则抛出异常
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    /// <returns>窗口实例</returns>
    private static HtmlMaskWindow GetWindowOrThrow(string windowId)
    {
        if (_windows.TryGetValue(windowId, out var window))
            return window;
        throw new InvalidOperationException($"HTML遮罩窗口不存在或已关闭: {windowId}");
    }

    /// <summary>
    /// 设置指定窗口的点击穿透模式
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    /// <param name="enabled">true=点击穿透，false=可交互</param>
    public static void SetClickThrough(string windowId, bool enabled)
    {
        GetWindowOrThrow(windowId).SetClickThroughMode(enabled);
    }

    /// <summary>
    /// 获取指定窗口的点击穿透状态
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    /// <returns>点击穿透状态</returns>
    public static bool GetClickThrough(string windowId)
    {
        return GetWindowOrThrow(windowId).IsClickThrough;
    }

    /// <summary>
    /// 原子切换指定窗口的点击穿透模式
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    public static void ToggleClickThrough(string windowId)
    {
        GetWindowOrThrow(windowId).ToggleClickThroughCore();
    }

    /// <summary>
    /// 通知窗口刷新待推送的消息
    /// </summary>
    internal static void NotifyFlush(string windowId)
    {
        if (!_windows.TryGetValue(windowId, out var window)) return;
        window.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // 页面还没加载完，消息留在队列中由 NavigationCompleted 统一推送
                if (!window._navigationCompleted) return;
                if (window.WebView.CoreWebView2 == null) return;
                HtmlMask.FlushPendingMessages(windowId, json =>
                {
                    window.WebView.CoreWebView2.PostWebMessageAsString(json);
                });
            }
            catch (ObjectDisposedException)
            {
                // WebView 已被释放，忽略此消息推送
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogDebug(ex, "HTML遮罩窗口消息推送异常");
            }
        });
    }

    #endregion

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetClickThrough(true);
        UpdatePosition();

        if (_initializationTask != null)
        {
            return;
        }

        _initializationTask = InitializeAsync();
        await _initializationTask;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, _webView2DataPath);
            if (_isClosing) return;

            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = HtmlMaskProfileName;
            await WebView.EnsureCoreWebView2Async(environment, controllerOptions);
            if (_isClosing) return;

            WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            WebView.CoreWebView2.Settings.IsScriptEnabled = true;
            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            if (_virtualHostName != null)
            {
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    _virtualHostName,
                    _workDir,
                    CoreWebView2HostResourceAccessKind.Deny);
            }

            // 拦截网络请求，仅允许注册过的域名
            WebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

            // 注入 helper JS，提供 window.htmlMask.request / onMessage API
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                window.htmlMask = {
                    _callbacks: {},
                    _seq: 0,
                    request: function(url, data) {
                        return new Promise(function(resolve, reject) {
                            var id = '__req_' + (++window.htmlMask._seq);
                            window.htmlMask._callbacks[id] = { resolve: resolve, reject: reject };
                            window.chrome.webview.postMessage(JSON.stringify({
                                url: url,
                                data: data || {},
                                requestId: id
                            }));
                        });
                    },
                    onMessage: null,
                    _dispatch: function(raw) {
                        try {
                            var msg = JSON.parse(raw);
                            if (msg.requestId && window.htmlMask._callbacks[msg.requestId]) {
                                window.htmlMask._callbacks[msg.requestId].resolve(msg);
                                delete window.htmlMask._callbacks[msg.requestId];
                            } else if (window.htmlMask.onMessage) {
                                var result = window.htmlMask.onMessage(msg);
                                if (msg.requestId && result !== undefined) {
                                    Promise.resolve(result).then(function(data) {
                                        window.chrome.webview.postMessage(JSON.stringify({
                                            requestId: msg.requestId,
                                            url: '/__response__',
                                            data: data
                                        }));
                                    });
                                }
                            }
                        } catch(e) {
                            if (window.htmlMask.onMessage) window.htmlMask.onMessage(raw);
                        }
                    }
                };
                window.chrome.webview.addEventListener('message', function(e) {
                    window.htmlMask._dispatch(e.data);
                });
            ");
            if (_isClosing) return;

            // 监听HTML发来的消息，解析 url + data + requestId
            WebView.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    string raw = e.TryGetWebMessageAsString();
                    string messageUrl = "";
                    string data = raw;
                    string? requestId = null;

                    try
                    {
                        using var doc = JsonDocument.Parse(raw);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("url", out var ep))
                        {
                            messageUrl = ep.GetString() ?? "";
                            data = root.TryGetProperty("data", out var d) ? d.GetRawText() : "{}";
                        }
                        if (root.TryGetProperty("requestId", out var rid))
                        {
                            requestId = rid.GetString();
                        }
                    }
                    catch { }

                    HtmlMask.SendFromHtml(_id, messageUrl, data, requestId);
                }
                catch { }
            };

            // 页面加载完成后推送队列中待发送的消息
            WebView.CoreWebView2.NavigationStarting += (_, _) =>
            {
                _navigationCompleted = false;
            };
            WebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _navigationCompleted = true;
                HtmlMask.FlushPendingMessages(_id, json =>
                {
                    WebView.CoreWebView2.PostWebMessageAsString(json);
                });
            };

            if (!string.IsNullOrEmpty(_pageUrl))
            {
                WebView.Source = new Uri(_pageUrl);
            }
        }
        catch (Exception e) when (_isClosing)
        {
            // 窗口关闭会销毁 WebView2 的宿主句柄，尚未完成的初始化会以 E_ABORT 结束。
            TaskControl.Logger.LogDebug(e, "HTML遮罩窗口已关闭，WebView2 初始化已取消");
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogError(e, "WebView2 初始化失败");
            if (!_isClosing)
            {
                Close();
            }
        }
    }

    private bool ReloadCore()
    {
        if (_isClosing)
        {
            return false;
        }

        try
        {
            // 初始化尚未完成时，首次导航会直接读取磁盘上的最新内容。
            if (WebView.CoreWebView2 == null)
            {
                return true;
            }

            _navigationCompleted = false;
            WebView.CoreWebView2.Reload();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 拦截网络请求，仅允许 file://、data:// 和注册过的域名
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = new Uri(e.Request.Uri);

            // 允许数据URI
            if (uri.Scheme == "data") return;

            // 虚拟主机只映射当前脚本目录，允许页面及其相对资源正常加载
            if (_virtualHostName != null
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host.Equals(_virtualHostName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 本地文件：必须在脚本目录内
            if (uri.Scheme == "file")
            {
                var localPath = uri.LocalPath;
                var fullDir = Path.GetFullPath(_workDir);
                var fullFile = Path.GetFullPath(localPath);
                if (fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase)) return;
                TaskControl.Logger.LogWarning("拦截HTML遮罩越级文件访问: {Uri}", e.Request.Uri);
                e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                return;
            }

            // 仅允许页面自身的初始导航
            if (string.Equals(uri.AbsoluteUri, _pageUrl, StringComparison.OrdinalIgnoreCase)) return;

            // HTTP/HTTPS 请求：与 JS 脚本使用完全一致的权限校验
            var currentProject = TaskContext.Instance().CurrentScriptProject;
            if (currentProject?.AllowJsHTTP != true)
            {
                TaskControl.Logger.LogWarning("未启用JS HTTP权限，拦截HTML遮罩网络请求: {Uri}", e.Request.Uri);
                e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                return;
            }

            var allowedUrls = currentProject?.Project?.Manifest.HttpAllowedUrls ?? [];
            if (allowedUrls.Length == 0)
            {
                TaskControl.Logger.LogWarning("未配置 http_allowed_urls，拦截HTML遮罩网络请求: {Uri}", e.Request.Uri);
                e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                return;
            }

            if (allowedUrls.Any(allowedUrl =>
            {
                var pattern = "^" + Regex.Escape(allowedUrl).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(e.Request.Uri, pattern, RegexOptions.IgnoreCase);
            })) return;

            TaskControl.Logger.LogWarning("URL不在允许列表中，拦截HTML遮罩网络请求: {Uri}", e.Request.Uri);
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogWarning(ex, "HTML遮罩资源请求拦截异常");
        }
    }

    /// <summary>
    /// 更新窗口位置
    /// </summary>
    public void UpdatePosition()
    {
        try
        {
            var gameHandle = TaskContext.Instance().GameHandle;
            if (gameHandle == IntPtr.Zero) return;

            var currentRect = SystemControl.GetCaptureRect(gameHandle);
            if (currentRect.Width <= 0 || currentRect.Height <= 0) return;

            var dpiScale = DpiHelper.GetScale(gameHandle);
            Dispatcher.Invoke(() =>
            {
                Left = currentRect.Left / dpiScale.X;
                Top = currentRect.Top / dpiScale.Y;
                Width = currentRect.Width / dpiScale.X;
                Height = currentRect.Height / dpiScale.Y;
            });
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogDebug(ex, "HTML遮罩窗口位置更新失败");
        }
    }

    /// <summary>
    /// 设置点击穿透模式
    /// </summary>
    /// <param name="enabled">true=点击穿透，false=可交互</param>
    private void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (!_styleCaptured)
        {
            _originalStyle = (int)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
            _styleCaptured = true;
        }

        int newStyle = enabled
            ? (_originalStyle | (int)User32.WindowStylesEx.WS_EX_TRANSPARENT | (int)User32.WindowStylesEx.WS_EX_LAYERED)
            : _originalStyle;

        User32.SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, (IntPtr)newStyle);
        _isClickThrough = enabled;

        SetBackgroundOpacity(!enabled);

        if (!enabled)
        {
            // 禁用穿透：激活遮罩窗口，确保获得键盘和鼠标焦点
            try
            {
                User32.SetForegroundWindow(hwnd);
                User32.BringWindowToTop(hwnd);
                Dispatcher.Invoke(() => Activate());
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogDebug(ex, "HTML遮罩窗口激活失败");
            }
        }
        else
        {
            // 开启穿透：将焦点还给游戏窗口
            try
            {
                var gameHandle = TaskContext.Instance().GameHandle;
                if (gameHandle != IntPtr.Zero)
                {
                    SystemControl.FocusWindow(gameHandle);
                }
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogDebug(ex, "HTML遮罩恢复游戏焦点失败");
            }
        }
    }

    /// <summary>
    /// 设置背景透明度
    /// </summary>
    /// <param name="isInteractive">是否处于交互模式</param>
    private void SetBackgroundOpacity(bool isInteractive)
    {
        _backgroundBrush.Color = isInteractive
            ? System.Windows.Media.Color.FromArgb(1, 0, 0, 0)
            : System.Windows.Media.Color.FromArgb(0, 0, 0, 0);
    }

    /// <summary>
    /// 设置点击穿透模式
    /// </summary>
    /// <param name="enabled">true=点击穿透，false=可交互</param>
    public void SetClickThroughMode(bool enabled)
    {
        Dispatcher.Invoke(() => SetClickThrough(enabled));
    }

    /// <summary>
    /// 切换点击穿透模式
    /// </summary>
    private void ToggleClickThroughCore()
    {
        Dispatcher.Invoke(() => SetClickThrough(!_isClickThrough));
    }

    /// <summary>
    /// 释放 WebView2 资源，停止媒体播放
    /// </summary>
    private void DisposeWebView()
    {
        try
        {
            WebView.Dispose();
        }
        catch { }
    }
}
