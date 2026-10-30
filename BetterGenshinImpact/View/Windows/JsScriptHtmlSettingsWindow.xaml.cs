using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Script.Settings;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Infrastructure.WebView;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 展示脚本作者实现的 HTML 设置页。页面的所有请求都在本窗口的 WebView2 中拦截处理，不经过网络、不监听端口：
/// 本页主机的请求交给 <see cref="ScriptSettingsRequestHandler"/>，其他地址一律拒绝。
/// </summary>
public partial class JsScriptHtmlSettingsWindow : FluentWindow
{
    private readonly ILogger<JsScriptHtmlSettingsWindow> _logger = App.GetLogger<JsScriptHtmlSettingsWindow>();

    /// <summary>打开窗口时 BetterGI 是否为深色主题，决定页面的首选配色。</summary>
    private readonly bool _isDarkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    /// <summary>本次打开的设置页。</summary>
    private readonly ScriptSettingsHtmlPage _page;

    /// <summary>处理本页主机的全部请求。</summary>
    private readonly ScriptSettingsRequestHandler _handler;

    /// <summary>窗口视图模型。</summary>
    public JsScriptHtmlSettingsWindowViewModel ViewModel { get; }

    private JsScriptHtmlSettingsWindow(ScriptSettingsEditRequest request)
    {
        _page = request.Page;
        ViewModel = new JsScriptHtmlSettingsWindowViewModel(_page.ScriptName, _page.ScriptVersion);
        // 保存成功后在标题中显示时间
        _handler = new ScriptSettingsRequestHandler(request with
        {
            Save = settings =>
            {
                request.Save(settings);
                ViewModel.LastSavedTime = DateTime.Now;
            }
        }, _isDarkTheme ? "dark" : "light", RequestClose, _logger);

        DataContext = ViewModel;
        InitializeComponent();
        // 加载时先显示与主题一致的底色，避免白屏闪烁
        WebView.DefaultBackgroundColor = _isDarkTheme ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Loaded += async (_, _) => await InitializeWebViewAsync();
        Closed += (_, _) => WebView.Dispose();
    }

    /// <summary>
    /// 以模态方式打开设置页，关闭窗口后返回。页面每次保存都已经通过请求中的保存方法生效。
    /// </summary>
    /// <param name="request">设置页与读写方法。</param>
    /// <param name="owner">所属窗口；为空时使用主窗口。</param>
    /// <returns>无法打开时的原因；正常打开并关闭后为 null。</returns>
    public static string? Show(ScriptSettingsEditRequest request, Window? owner)
    {
        if (!WebViewRuntime.IsAvailable)
            return "未安装 Microsoft Edge WebView2 运行时";
        new JsScriptHtmlSettingsWindow(request) { Owner = owner ?? Application.Current.MainWindow }.ShowDialog();
        return null;
    }

    /// <summary>
    /// 初始化 WebView2：关闭页面不需要的能力、拦截全部请求，再打开入口。
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        try
        {
            var provider = App.GetService<IWebViewEnvironmentProvider>()
                           ?? throw new InvalidOperationException("WebView2 环境入口尚未初始化");
            // 不设置下载目录，页面发起的下载会被取消；target=_blank 的 http(s) 链接交给系统浏览器
            await WebView.InitializeAsync(provider, WebViewProfiles.ScriptSettings, new WebViewOptions
            {
                ProcessCrashed = _ => ViewModel.ErrorMessage = "网页进程异常退出，请关闭窗口后重新打开。"
            });

            var core = WebView.CoreWebView2;
            // 开发者工具（F12）和重新加载（F5）方便作者调试；页面只通过请求与 BetterGI 交互，也不保存表单数据
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Profile.PreferredColorScheme = _isDarkTheme
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.NavigationStarting += (_, e) => e.Cancel = !IsPageAddress(e.Uri);
            core.NavigationCompleted += OnNavigationCompleted;
            core.WindowCloseRequested += (_, _) => RequestClose();
            core.Navigate(_page.EntryUri.AbsoluteUri);
        }
        catch (Exception e)
        {
            // 初始化完成前关闭窗口也会走到这里，此时设置提示没有影响
            _logger.LogError(e, "脚本设置页初始化失败：{Script}", _page.ScriptName);
            ViewModel.ErrorMessage = "网页初始化失败：" + e.Message;
        }
    }

    /// <summary>
    /// 本页主机的请求交给处理器；其他地址（外网、本机服务等）一律返回 403。
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var response = IsPageAddress(e.Request.Uri)
                ? _handler.Handle(e.Request.Method, Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath),
                    e.Request.Content)
                : ScriptSettingsRequestHandler.Blocked;
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(response.Body), response.StatusCode, response.ReasonPhrase, response.Headers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "脚本设置页请求拦截异常：{Script}", _page.ScriptName);
        }
    }

    /// <summary>
    /// 入口或站内页面加载失败时显示原因；被拦截的跳转不算失败，页面保持原样。
    /// </summary>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            return;
        ViewModel.ErrorMessage = e.IsSuccess
            ? null
            : $"页面加载失败（HTTP {e.HttpStatusCode}，{e.WebErrorStatus}）。修改页面文件后可以在页面中按 F5 重新加载。";
    }

    /// <summary>
    /// 页面请求关闭窗口（window.close() 或 POST /__bgi/close）。推迟到当前 WebView2 事件结束后关闭，避免在事件中释放 WebView。
    /// </summary>
    private void RequestClose() => Dispatcher.BeginInvoke(() =>
    {
        // window.close() 与关闭接口可能先后到达，窗口已关闭时忽略
        if (IsLoaded)
            Close();
    });

    /// <summary>
    /// 是否为本设置页主机下的地址。
    /// </summary>
    private bool IsPageAddress(string address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && uri.Host.Equals(_page.HostName, StringComparison.OrdinalIgnoreCase);
}
