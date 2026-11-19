using System;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Infrastructure.WebView;
using BetterGenshinImpact.Service.HoYoLab;
using BetterGenshinImpact.Service.I18n;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 在 WebView2 中打开 HoYoLAB 官方地图页面供用户登录。登录成功后只读取 hoyolab.com 的 Cookie 保存，不接触密码。
/// </summary>
public partial class HoYoLabLoginWindow : FluentWindow
{
    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<HoYoLabLoginWindow> _logger = App.GetLogger<HoYoLabLoginWindow>();

    /// <summary>
    /// 打开的地图页面语言，如 pt-pt
    /// </summary>
    private readonly string _lang;

    /// <summary>
    /// 定时检查 Cookie，覆盖网页内登录不触发导航的情况
    /// </summary>
    private readonly DispatcherTimer _cookieTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// 是否正在处理登录结果，避免定时器与导航事件重复保存
    /// </summary>
    private bool _completing;

    /// <summary>
    /// 是否已登录成功
    /// </summary>
    private bool _loggedIn;

    /// <summary>
    /// 创建登录窗口
    /// </summary>
    /// <param name="lang">地图页面语言</param>
    private HoYoLabLoginWindow(string lang)
    {
        _lang = lang;
        InitializeComponent();
        var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        // 加载时先显示与主题一致的底色，避免白屏闪烁
        WebView.DefaultBackgroundColor = isDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Loaded += async (_, _) => await InitializeWebViewAsync();
        _cookieTimer.Tick += async (_, _) => await TryCompleteLoginAsync();
        Closed += (_, _) =>
        {
            _cookieTimer.Stop();
            WebView.Dispose();
        };
    }

    /// <summary>
    /// 以模态方式打开登录窗口
    /// </summary>
    /// <param name="owner">所属窗口；为空时使用主窗口</param>
    /// <param name="lang">地图页面语言</param>
    /// <returns>是否登录成功</returns>
    public static bool ShowLogin(Window? owner, string lang)
    {
        if (!WebViewRuntime.IsAvailable)
        {
            ThemedMessageBox.Warning(I18nService.Instance.Translate("未安装 Microsoft Edge WebView2 运行时"));
            return false;
        }

        var window = new HoYoLabLoginWindow(lang) { Owner = owner ?? Application.Current.MainWindow };
        window.ShowDialog();
        return window._loggedIn;
    }

    /// <summary>
    /// 初始化 WebView2：先清空本 Profile 的旧 Cookie（退出登录后需要重新登录），再打开地图页面
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        try
        {
            var provider = App.GetService<IWebViewEnvironmentProvider>()
                           ?? throw new InvalidOperationException("WebView2 环境入口尚未初始化");
            // 第三方登录（如 Google）弹窗在当前页面打开
            await WebView.InitializeAsync(provider, WebViewProfiles.HoYoLab, new WebViewOptions
            {
                NewWindowPolicy = WebViewNewWindowPolicy.NavigateInPlace
            });

            var core = WebView.CoreWebView2;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.CookieManager.DeleteAllCookies();
            core.NavigationCompleted += async (_, _) => await TryCompleteLoginAsync();
            core.Navigate($"https://act.hoyolab.com/ys/app/interactive-map/index.html?lang={Uri.EscapeDataString(_lang)}");
            _cookieTimer.Start();
        }
        catch (Exception e)
        {
            // 初始化完成前关闭窗口也会走到这里
            _logger.LogError(e, "HoYoLAB 登录窗口初始化失败");
            StatusText.Text = I18nService.Instance.Translate("网页初始化失败：") + e.Message;
        }
    }

    /// <summary>
    /// 读取 Cookie；出现 ltoken 即视为登录成功，保存会话并关闭窗口
    /// </summary>
    private async Task TryCompleteLoginAsync()
    {
        if (_completing || _loggedIn || WebView.CoreWebView2 == null)
        {
            return;
        }

        _completing = true;
        try
        {
            var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync("https://act.hoyolab.com");
            var header = HoYoLabSessionStore.BuildCookieHeader(
                cookies.Select(c => new HoYoLabCookie(c.Name, c.Value, c.Domain)));
            if (header == null)
            {
                return;
            }

            _cookieTimer.Stop();
            var nickname = await TryGetNicknameAsync(header);
            App.GetService<HoYoLabAccountService>()!.SetSession(header, nickname);
            _loggedIn = true;
            _logger.LogInformation("HoYoLAB 登录成功");
            Close();
        }
        catch (Exception e)
        {
            // 窗口关闭过程中 WebView 已释放时也会走到这里
            _logger.LogDebug(e, "读取 HoYoLAB Cookie 失败");
        }
        finally
        {
            _completing = false;
        }
    }

    /// <summary>
    /// 获取账号昵称，仅用于显示；失败返回 null
    /// </summary>
    private async Task<string?> TryGetNicknameAsync(string cookieHeader)
    {
        try
        {
            var client = App.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.GameApi);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://bbs-api-os.hoyolab.com/community/painter/wapi/user/full");
            request.Headers.Referrer = new Uri("https://act.hoyolab.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://act.hoyolab.com");
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            using var response = await client.SendAsync(request);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            return json["data"]?["user_info"]?["nickname"]?.Value<string>();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "获取 HoYoLAB 昵称失败");
            return null;
        }
    }
}
