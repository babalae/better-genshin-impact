using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Infrastructure.WebView;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.View.Controls.Webview;

/// <summary>
/// WebView2 运行时缺失时的提示：说明原因，并可打开官方下载页（见设计文档 8.4 节）
/// </summary>
public static class WebViewRuntimePrompt
{
    /// <summary>
    /// 提示文字
    /// </summary>
    private const string Message = "该功能需要 Microsoft Edge WebView2 运行时，当前电脑没有安装。\n" +
                                   "点击“确定”打开官方下载页面，安装完成后重新打开该功能即可。";

    /// <summary>
    /// 运行时可用时返回 true；不可用时弹出说明，用户确认后打开下载页，返回 false
    /// </summary>
    public static async Task<bool> EnsureAvailableAsync()
    {
        if (WebViewRuntime.IsAvailable)
        {
            return true;
        }

        var result = await ThemedMessageBox.WarningAsync(Message, "缺少 WebView2 运行时", MessageBoxButton.OKCancel,
            MessageBoxResult.OK);
        if (result == MessageBoxResult.OK)
        {
            OpenDownloadPage();
        }

        return false;
    }

    /// <summary>
    /// 用系统浏览器打开运行时下载页
    /// </summary>
    public static void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WebViewRuntime.DownloadUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error($"无法打开浏览器：{e.Message}\n请手动访问 {WebViewRuntime.DownloadUrl}");
        }
    }
}
