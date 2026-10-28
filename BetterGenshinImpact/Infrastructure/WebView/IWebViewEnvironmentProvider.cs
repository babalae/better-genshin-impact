using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// WebView2 环境的唯一创建入口（见设计文档 8.3 节）。环境对象只能在界面线程上创建和使用
/// </summary>
public interface IWebViewEnvironmentProvider
{
    /// <summary>
    /// 获取普通环境，每个进程每个环境只创建一次
    /// </summary>
    Task<CoreWebView2Environment> GetAsync(WebViewEnvironmentDefinition environment);

    /// <summary>
    /// 创建云原神实例的环境。只能在同名的网页版实例进程中调用，否则抛出 <see cref="System.InvalidOperationException"/>
    /// </summary>
    Task<CoreWebView2Environment> CreateCloudGameAsync(string instanceName);

    /// <summary>
    /// 记录网页进程异常退出，供 <see cref="WebViewSetup"/> 和云原神宿主窗口统一写日志
    /// </summary>
    /// <param name="source">网页的用途，例如“软件内网页 · 网页缓存”或“云原神 · 小号A”</param>
    /// <param name="e">事件参数</param>
    void ReportProcessFailed(string source, CoreWebView2ProcessFailedEventArgs e);
}
