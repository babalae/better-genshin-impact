using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// WPF WebView2 控件的统一初始化：使用 Profile 所属的普通环境，并套用通用设置和事件处理（见设计文档 8.3 节）。
/// 各使用方的特有逻辑（虚拟主机映射、HostObject、资源拦截、消息桥）在初始化完成后自行添加
/// </summary>
public static class WebViewSetup
{
    /// <summary>
    /// 用 Profile 所属的普通环境初始化控件。必须在界面线程调用，且控件尚未初始化（未设置 Source）
    /// </summary>
    /// <param name="webView">WPF WebView2 控件</param>
    /// <param name="provider">环境入口</param>
    /// <param name="profile">使用的 Profile</param>
    /// <param name="options">选项，为空时使用默认值</param>
    public static async Task InitializeAsync(this WebView2 webView, IWebViewEnvironmentProvider provider,
        WebViewProfile profile, WebViewOptions? options = null)
    {
        options ??= new WebViewOptions();
        var environment = await provider.GetAsync(profile.Environment);
        if (profile.IsDefault)
        {
            await webView.EnsureCoreWebView2Async(environment);
        }
        else
        {
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = profile.ProfileName;
            await webView.EnsureCoreWebView2Async(environment, controllerOptions);
        }

        var core = webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = RuntimeHelper.IsDebug;
        core.Settings.IsStatusBarEnabled = false;
        core.NewWindowRequested += (_, e) => OnNewWindowRequested(e, options);
        core.DownloadStarting += (_, e) => OnDownloadStarting(e, options);
        AttachProcessFailedHandler(core, provider, profile.DisplayName, options);
    }

    /// <summary>
    /// 新窗口只把 http、https 地址交给系统浏览器，其他一律拒绝，不在程序内弹出浏览器窗口
    /// </summary>
    private static void OnNewWindowRequested(CoreWebView2NewWindowRequestedEventArgs e, WebViewOptions options)
    {
        e.Handled = true;
        if (!options.OpenNewWindowInBrowser
            || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            // 系统没有默认浏览器等情况下忽略，页面保持不变
        }
    }

    /// <summary>
    /// 没有下载目录时取消下载；有目录时保存到该目录，文件名沿用网页给出的名称
    /// </summary>
    private static void OnDownloadStarting(CoreWebView2DownloadStartingEventArgs e, WebViewOptions options)
    {
        if (string.IsNullOrEmpty(options.DownloadFolder))
        {
            e.Cancel = true;
            return;
        }

        Directory.CreateDirectory(options.DownloadFolder);
        e.ResultFilePath = Path.Combine(options.DownloadFolder, Path.GetFileName(e.ResultFilePath));
    }

    /// <summary>
    /// 网页进程异常退出：记录日志；渲染进程第一次退出时自动重新加载，再次退出或浏览器进程退出时交给界面提示
    /// </summary>
    private static void AttachProcessFailedHandler(CoreWebView2 core, IWebViewEnvironmentProvider provider,
        string source, WebViewOptions options)
    {
        var reloaded = false;
        core.ProcessFailed += (_, e) =>
        {
            provider.ReportProcessFailed(source, e);
            switch (e.ProcessFailedKind)
            {
                case CoreWebView2ProcessFailedKind.RenderProcessExited when !reloaded:
                    reloaded = true;
                    try
                    {
                        core.Reload();
                    }
                    catch (Exception)
                    {
                        options.ProcessCrashed?.Invoke(e.ProcessFailedKind);
                    }

                    break;
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    options.ProcessCrashed?.Invoke(e.ProcessFailedKind);
                    break;
                // 页面无响应、子框架或工具进程退出时页面通常还能继续使用，只记录日志
            }
        };
    }
}
