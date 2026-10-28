using System;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// WebView2 运行时检测。提示用户安装的界面由 View 层的 WebViewRuntimePrompt 负责
/// </summary>
public static class WebViewRuntime
{
    /// <summary>
    /// Microsoft Edge WebView2 运行时的官方下载页
    /// </summary>
    public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>
    /// 已安装的运行时版本，未安装或无法检测时为 null
    /// </summary>
    public static string? GetVersion()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (Exception)
        {
            // 未安装时抛出 WebView2RuntimeNotFoundException，其他异常同样视为不可用
            return null;
        }
    }

    /// <summary>
    /// 运行时是否可用
    /// </summary>
    public static bool IsAvailable => GetVersion() is not null;
}
