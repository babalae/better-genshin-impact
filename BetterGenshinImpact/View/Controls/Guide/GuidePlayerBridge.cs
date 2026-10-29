using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Infrastructure.WebView;
using BetterGenshinImpact.Service.Guide;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.View.Controls.Guide;

/// <summary>
/// 悬浮小窗播放桥的宿主侧：初始化网页、注入页面脚本、解析页面消息、执行播放命令（见设计文档 7.1、7.4 节）。
/// 只在界面线程使用
/// </summary>
public sealed class GuidePlayerBridge : IGuidePlayer, IDisposable
{
    /// <summary>
    /// 页面脚本资源，按顺序注入：先登记站点适配，再运行播放桥
    /// </summary>
    private static readonly string[] ScriptResources =
    [
        "BetterGenshinImpact.Resources.JavaScript.guide-site-bilibili.js",
        "BetterGenshinImpact.Resources.JavaScript.guide-bridge.js",
    ];

    /// <summary>
    /// 页面脚本内容，进程内只读取一次
    /// </summary>
    private static readonly Lazy<string[]> Scripts = new(() => Array.ConvertAll(ScriptResources, ReadResource));

    private readonly ILogger<GuidePlayerBridge> _logger;

    /// <summary>
    /// WebView2 环境入口，初始化时取得悬浮小窗环境
    /// </summary>
    private readonly IWebViewEnvironmentProvider _environmentProvider;

    /// <summary>
    /// 承载网页的控件
    /// </summary>
    private readonly WebView2CompositionControl _webView;

    /// <summary>
    /// 初始化任务，只执行一次
    /// </summary>
    private Task? _initialization;

    /// <summary>
    /// 初始化完成前请求打开的网址
    /// </summary>
    private string? _pendingUrl;

    /// <summary>
    /// 网页缩放比例
    /// </summary>
    private double _zoom = 1;

    /// <summary>
    /// 已释放
    /// </summary>
    private bool _disposed;

    /// <param name="webView">承载网页的控件</param>
    /// <param name="environmentProvider">WebView2 环境入口</param>
    /// <param name="logger">日志</param>
    public GuidePlayerBridge(WebView2CompositionControl webView, IWebViewEnvironmentProvider environmentProvider,
        ILogger<GuidePlayerBridge> logger)
    {
        _webView = webView;
        _environmentProvider = environmentProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public GuidePlayerState? State { get; private set; }

    /// <inheritdoc />
    public string? CurrentUrl => Core?.Source;

    /// <inheritdoc />
    public bool CanGoBack => Core?.CanGoBack ?? false;

    /// <inheritdoc />
    public bool IsMuted
    {
        get => Core?.IsMuted ?? false;
        set
        {
            if (Core is { } core)
            {
                core.IsMuted = value;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<GuidePlayerState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<string>? SubtitleChanged;

    /// <inheritdoc />
    public event EventHandler<GuidePlayerEvent>? EventRaised;

    /// <summary>
    /// 已初始化的网页核心，未初始化或已释放时为 null
    /// </summary>
    private CoreWebView2? Core => _disposed ? null : _webView.CoreWebView2;

    /// <summary>
    /// 开始初始化（只执行一次）
    /// </summary>
    public Task EnsureInitializedAsync() => _initialization ??= InitializeAsync();

    /// <summary>
    /// 用悬浮小窗环境初始化网页：新窗口在小窗内打开、拒绝下载与权限请求、拦截外部协议，注入页面脚本
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            await _webView.InitializeAsync(_environmentProvider, WebViewProfiles.Guide, new WebViewOptions
            {
                NewWindowPolicy = WebViewNewWindowPolicy.NavigateInPlace,
                ProcessCrashed = OnProcessCrashed,
            });
            if (_disposed)
            {
                return;
            }

            var core = _webView.CoreWebView2;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.PermissionRequested += OnPermissionRequested;
            core.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += OnWebMessageReceived;
            foreach (var script in Scripts.Value)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            }

            if (_disposed)
            {
                return;
            }

            _webView.ZoomFactor = _zoom;
            if (_pendingUrl is { } url)
            {
                _pendingUrl = null;
                core.Navigate(url);
            }
        }
        catch (Exception e) when (_disposed)
        {
            // 初始化过程中关闭了小窗，WebView2 会以 E_ABORT 结束
            _logger.LogDebug(e, "悬浮小窗已关闭，网页初始化已取消");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "悬浮小窗网页初始化失败");
            EventRaised?.Invoke(this, new GuidePlayerEvent(GuidePlayerEventKind.InitializeFailed, e.Message));
        }
    }

    /// <inheritdoc />
    public void Navigate(string url)
    {
        if (Core is { } core)
        {
            try
            {
                core.Navigate(url);
            }
            catch (ArgumentException e)
            {
                _logger.LogDebug(e, "悬浮小窗打开网址失败：{Url}", url);
            }

            return;
        }

        // 网页控件加载后才初始化（窗口 Loaded 时开始），初始化完成后打开这个网址
        _pendingUrl = url;
        if (_webView.IsLoaded)
        {
            _ = EnsureInitializedAsync();
        }
    }

    /// <inheritdoc />
    public void GoBack()
    {
        if (Core is { CanGoBack: true } core)
        {
            core.GoBack();
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        Core?.Reload();
    }

    /// <inheritdoc />
    public void SetZoom(double factor)
    {
        _zoom = factor;
        if (Core is not null)
        {
            _webView.ZoomFactor = factor;
        }
    }

    /// <inheritdoc />
    public async Task<GuideCommandResult> ExecuteAsync(GuidePlayerCommand command, double value = 0)
    {
        if (Core is not { } core)
        {
            return GuideCommandResult.NotReady;
        }

        // 参数先序列化为 JSON，不把任何字符串直接拼进脚本
        var payload = JsonConvert.SerializeObject(new { cmd = CommandName(command), value });
        try
        {
            var json = await core.ExecuteScriptAsync($"window.__bgiGuide ? window.__bgiGuide.exec({payload}) : null");
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                return GuideCommandResult.NotReady;
            }

            var result = JObject.Parse(json);
            return new GuideCommandResult(result.Value<bool?>("ok") ?? false, result.Value<string?>("reason"));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ObjectDisposedException
                                      or System.Runtime.InteropServices.COMException)
        {
            _logger.LogDebug(e, "悬浮小窗执行命令 {Command} 失败", command);
            return new GuideCommandResult(false, "notReady");
        }
    }

    /// <summary>
    /// 命令对应的页面脚本名称
    /// </summary>
    private static string CommandName(GuidePlayerCommand command) => command switch
    {
        GuidePlayerCommand.Play => "play",
        GuidePlayerCommand.Pause => "pause",
        GuidePlayerCommand.Toggle => "toggle",
        GuidePlayerCommand.SeekBy => "seekBy",
        GuidePlayerCommand.SeekTo => "seekTo",
        GuidePlayerCommand.SetRate => "setRate",
        GuidePlayerCommand.SetVolume => "setVolume",
        GuidePlayerCommand.PrevEpisode => "prevEpisode",
        GuidePlayerCommand.NextEpisode => "nextEpisode",
        GuidePlayerCommand.SetImmersive => "setImmersive",
        GuidePlayerCommand.SetAutoSubtitle => "setAutoSubtitle",
        GuidePlayerCommand.SetPageSubtitleHidden => "setPageSubtitleHidden",
        GuidePlayerCommand.SetDanmaku => "setDanmaku",
        _ => "requestState",
    };

    /// <summary>
    /// 页面消息：只接受 state、subtitle、event 三类，数值做范围校验
    /// </summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JObject message;
        try
        {
            message = JObject.Parse(e.WebMessageAsJson);
        }
        catch (JsonException)
        {
            return;
        }

        switch (message.Value<string?>("type"))
        {
            case "state":
                State = ParseState(message);
                StateChanged?.Invoke(this, State);
                break;
            case "subtitle":
                var text = message.Value<string?>("text") ?? string.Empty;
                SubtitleChanged?.Invoke(this, text.Length > 500 ? text[..500] : text);
                break;
            case "event":
                if (ParseEvent(message.Value<string?>("name")) is { } kind)
                {
                    EventRaised?.Invoke(this, new GuidePlayerEvent(kind));
                }

                break;
        }
    }

    /// <summary>
    /// 解析状态消息
    /// </summary>
    private static GuidePlayerState ParseState(JObject message)
    {
        var href = Limit(message.Value<string?>("href"), 2048);
        var hasVideo = message.Value<bool?>("hasVideo") ?? false;
        var key = Limit(message.Value<string?>("videoKey"), 512);
        // B 站由站点适配给出标识；其他网站按网址计算，与收藏、观看记录的规则一致
        if (hasVideo && (string.IsNullOrEmpty(key) || !key.StartsWith("bilibili:", StringComparison.Ordinal)))
        {
            key = GuideLinkResolver.GetVideoKey(href);
        }

        return new GuidePlayerState
        {
            Href = href,
            Title = Limit(message.Value<string?>("title"), 300),
            Site = Limit(message.Value<string?>("site"), 32),
            VideoKey = hasVideo || key?.StartsWith("bilibili:", StringComparison.Ordinal) == true ? key : null,
            HasVideo = hasVideo,
            Paused = message.Value<bool?>("paused") ?? true,
            CurrentTime = Number(message, "currentTime", 0, 1e7),
            Duration = Number(message, "duration", 0, 1e7),
            PlaybackRate = Number(message, "playbackRate", 0.05, 16, 1),
            Volume = Number(message, "volume", 0, 1, 1),
            VideoWidth = Number(message, "videoWidth", 0, 20000),
            VideoHeight = Number(message, "videoHeight", 0, 20000),
            Immersive = message.Value<bool?>("immersive") ?? false,
            EpisodeIndex = (int)Number(message, "episodeIndex", 0, 100000),
            EpisodeCount = (int)Number(message, "episodeCount", 0, 100000),
            HasPrevEpisode = message.Value<bool?>("hasPrev") ?? false,
            HasNextEpisode = message.Value<bool?>("hasNext") ?? false,
            Danmaku = message["danmaku"]?.Type == JTokenType.Boolean ? message.Value<bool>("danmaku") : null,
            ReceivedAt = Environment.TickCount64,
        };
    }

    /// <summary>
    /// 读取数值并夹到范围内，缺失或非法时使用默认值
    /// </summary>
    private static double Number(JObject message, string name, double min, double max, double fallback = 0)
    {
        var token = message[name];
        if (token?.Type is not (JTokenType.Float or JTokenType.Integer))
        {
            return fallback;
        }

        var value = token.Value<double>();
        return double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }

    /// <summary>
    /// 截断过长的字符串
    /// </summary>
    private static string Limit(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length > max ? value[..max] : value;

    /// <summary>
    /// 页面事件名转为事件类型，未知事件忽略
    /// </summary>
    private static GuidePlayerEventKind? ParseEvent(string? name) => name switch
    {
        "ready" => GuidePlayerEventKind.Ready,
        "videoChanged" => GuidePlayerEventKind.VideoChanged,
        "ended" => GuidePlayerEventKind.Ended,
        "playRejected" => GuidePlayerEventKind.PlayRejected,
        "editFocus" => GuidePlayerEventKind.EditFocus,
        "editBlur" => GuidePlayerEventKind.EditBlur,
        "immersiveExited" => GuidePlayerEventKind.ImmersiveExited,
        _ => null,
    };

    /// <summary>
    /// 只允许 http、https 和空白页导航，其他协议一律取消
    /// </summary>
    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") && !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    /// <summary>
    /// 网页尝试打开 bilibili:// 等客户端协议时取消
    /// </summary>
    private static void OnLaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
    {
        e.Cancel = true;
    }

    /// <summary>
    /// 通知、摄像头、麦克风、读取剪贴板、地理位置等权限一律拒绝
    /// </summary>
    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
    }

    /// <summary>
    /// 网页进程反复崩溃或浏览器进程退出
    /// </summary>
    private void OnProcessCrashed(CoreWebView2ProcessFailedKind kind)
    {
        EventRaised?.Invoke(this, new GuidePlayerEvent(kind == CoreWebView2ProcessFailedKind.BrowserProcessExited
            ? GuidePlayerEventKind.BrowserExited
            : GuidePlayerEventKind.Crashed));
    }

    /// <summary>
    /// 读取嵌入的页面脚本
    /// </summary>
    private static string ReadResource(string name)
    {
        using var stream = typeof(GuidePlayerBridge).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"找不到嵌入资源 {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 释放网页，停止播放并结束网页进程
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _webView.Dispose();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "释放悬浮小窗网页失败");
        }
    }
}
