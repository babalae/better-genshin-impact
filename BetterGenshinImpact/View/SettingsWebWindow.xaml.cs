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
using BetterGenshinImpact.Core.Script.Dependence;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.View;

public partial class SettingsWebWindow : Window
{
    private static readonly ConcurrentDictionary<string, SettingsWebWindow> Windows = new();
    private const string ProfileName = "SettingsWebUi";

    private readonly HtmlMessageChannel _channel = new();
    private readonly string _id;
    private readonly string _workDir;
    private readonly string? _virtualHostName;
    private readonly string _pageUrl;
    private readonly bool _allowHttp;
    private readonly string[] _httpAllowedUrls;
    private bool _navigationCompleted;
    private bool _isClosing;

    private SettingsWebWindow(string htmlPath, string workDir, bool allowHttp, string[] httpAllowedUrls, string? id)
    {
        _id = id ?? Guid.NewGuid().ToString("N");
        _workDir = Path.GetFullPath(workDir);
        _allowHttp = allowHttp;
        _httpAllowedUrls = httpAllowedUrls;
        var scriptName = Path.GetFileName(Path.TrimEndingDirectorySeparator(_workDir));
        _virtualHostName = $"sw-{CreateScriptKey(scriptName)}.bettergi.local";
        _pageUrl = CreateVirtualPageUrl(new Uri(Path.GetFullPath(htmlPath)), _workDir, _virtualHostName);
        InitializeComponent();
        Closing += (_, _) => _isClosing = true;
    }

    public static string Show(string htmlPath, string workDir, bool allowHttp, string[] httpAllowedUrls, string sessionKey)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            if (Windows.TryGetValue(sessionKey, out var existing))
            {
                existing.Activate();
                return existing._id;
            }

            var window = new SettingsWebWindow(htmlPath, workDir, allowHttp, httpAllowedUrls, sessionKey);
            Windows[sessionKey] = window;
            window._channel.Register(window._id, windowId => window.Dispatcher.BeginInvoke(() => window.Flush(windowId)));
            window.Closed += (_, _) =>
            {
                Windows.TryRemove(sessionKey, out _);
                window._channel.Unregister(window._id);
            };
            window.Show();
            _ = window.InitializeAsync();
            return window._id;
        });
    }

    public static bool Exists(string id) => Windows.Values.Any(window => window._id == id);

    public static HtmlMessageChannel? Channel(string id) => Windows.Values.FirstOrDefault(window => window._id == id)?._channel;

    public static bool Activate(string sessionKey)
    {
        if (!Windows.TryGetValue(sessionKey, out var window)) return false;
        window.Dispatcher.BeginInvoke(window.Activate);
        return true;
    }

    public static bool Close(string id)
    {
        var window = Windows.Values.FirstOrDefault(item => item._id == id);
        if (window == null) return false;
        window.Dispatcher.BeginInvoke(window.Close);
        return true;
    }

    public static void ShowError(string id, string message)
    {
        var window = Windows.Values.FirstOrDefault(item => item._id == id);
        window?.Dispatcher.BeginInvoke(() =>
        {
            window.Title = "JS脚本HTML配置启动失败";
            MessageBox.Show(window, message, "JS脚本HTML配置启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppContext.BaseDirectory, "WebView2Data"));
            if (_isClosing) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = ProfileName;
            await WebView.EnsureCoreWebView2Async(environment, options);
            if (_isClosing) return;

            WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(_virtualHostName!, _workDir, CoreWebView2HostResourceAccessKind.Deny);
            WebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(PageHelperScript);
            WebView.CoreWebView2.WebMessageReceived += (_, e) => Receive(e.TryGetWebMessageAsString());
            WebView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                _navigationCompleted = false;
                if (!IsOwnedNavigation(args.Uri)) args.Cancel = true;
            };
            WebView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            WebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _navigationCompleted = true;
                Flush(_id);
            };
            WebView.Source = new Uri(_pageUrl);
        }
        catch (Exception e)
        {
            App.GetLogger<SettingsWebWindow>().LogError(e, "HTML配置窗口初始化失败");
            if (!_isClosing) Close();
        }
    }

    private void Flush(string windowId)
    {
        if (!_navigationCompleted || WebView.CoreWebView2 == null) return;
        _channel.Flush(windowId, json => WebView.CoreWebView2.PostWebMessageAsString(json));
    }

    private void Receive(string raw)
    {
        var url = "";
        var data = raw;
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.TryGetProperty("url", out var urlElement))
            {
                url = urlElement.GetString() ?? "";
                data = root.TryGetProperty("data", out var dataElement) ? dataElement.GetRawText() : "{}";
            }

            if (root.TryGetProperty("requestId", out var requestElement)) requestId = requestElement.GetString();
        }
        catch
        {
            // 非 JSON 消息按原文本进入队列，由配置后端决定如何处理。
        }

        _channel.SendFromHtml(_id, url, data, requestId);
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        if (uri.Scheme == "data" || IsVirtualHost(uri) || string.Equals(uri.AbsoluteUri, _pageUrl, StringComparison.OrdinalIgnoreCase)) return;
        if (!_allowHttp || !IsAllowed(uri.AbsoluteUri))
        {
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        }
    }

    private bool IsOwnedNavigation(string uri) => IsVirtualHost(new Uri(uri));

    private bool IsVirtualHost(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals(_virtualHostName, StringComparison.OrdinalIgnoreCase);

    private bool IsAllowed(string uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var target) && _httpAllowedUrls.Any(allowed => IsAllowed(target, allowed));

    private static bool IsAllowed(Uri target, string allowed)
    {
        var marker = allowed.IndexOf("://", StringComparison.Ordinal);
        var path = marker < 0 ? -1 : allowed.IndexOf('/', marker + 3);
        var origin = path < 0 ? allowed : allowed[..path];
        var remainder = path < 0 ? "/" : allowed[path..];
        if (!Uri.TryCreate(origin.Replace("*", "placeholder") + "/", UriKind.Absolute, out var pattern)) return false;
        return target.Scheme.Equals(pattern.Scheme, StringComparison.OrdinalIgnoreCase) &&
               Regex.IsMatch(target.Authority, "^" + Regex.Escape(pattern.Authority.Replace("placeholder", "*")).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase) &&
               Regex.IsMatch(target.PathAndQuery, "^" + Regex.Escape(remainder).Replace("\\*", ".*") + "$");
    }

    private static string CreateScriptKey(string scriptName)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scriptName.ToUpperInvariant())).AsSpan(0, 4)).ToLowerInvariant();

    private static string CreateVirtualPageUrl(Uri fileUri, string workDir, string virtualHostName)
    {
        var relativePath = Path.GetRelativePath(workDir, fileUri.LocalPath);
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"HTML页面路径越界访问: {fileUri.LocalPath}");
        }

        var escapedPath = string.Join('/', relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString));
        return new UriBuilder(Uri.UriSchemeHttps, virtualHostName) { Path = "/" + escapedPath }.Uri.AbsoluteUri;
    }

    private const string PageHelperScript = @"
        window.settingsUi = {
            _callbacks: {}, _seq: 0, _queue: [],
            post: function(url, data) {
                window.chrome.webview.postMessage(JSON.stringify({ url: url, data: data || {} }));
            },
            request: function(url, data) {
                return new Promise(function(resolve) {
                    var id = '__req_' + (++window.settingsUi._seq);
                    window.settingsUi._callbacks[id] = { resolve: resolve };
                    window.chrome.webview.postMessage(JSON.stringify({ url: url, data: data || {}, requestId: id }));
                });
            },
            onMessage: null,
            _dispatch: function(raw) {
                var msg = JSON.parse(raw);
                if (msg.requestId && window.settingsUi._callbacks[msg.requestId]) {
                    window.settingsUi._callbacks[msg.requestId].resolve(msg);
                    delete window.settingsUi._callbacks[msg.requestId];
                } else if (window.settingsUi.onMessage) {
                    var result = window.settingsUi.onMessage(msg);
                    if (msg.requestId && result !== undefined) {
                        Promise.resolve(result).then(function(data) {
                            window.chrome.webview.postMessage(JSON.stringify({ requestId: msg.requestId, url: '/__response__', data: data }));
                        });
                    }
                } else {
                    window.settingsUi._queue.push(msg);
                }
            }
        };
        Object.defineProperty(window.settingsUi, 'onMessage', {
            configurable: true,
            get: function() { return window.settingsUi._onMessage || null; },
            set: function(handler) {
                window.settingsUi._onMessage = handler;
                var queued = window.settingsUi._queue.splice(0);
                queued.forEach(function(msg) { window.settingsUi._dispatch(JSON.stringify(msg)); });
            }
        });
        window.chrome.webview.addEventListener('message', function(e) { window.settingsUi._dispatch(e.data); });
    ";
}
