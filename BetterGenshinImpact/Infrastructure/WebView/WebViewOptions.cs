using System;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// 普通网页的初始化选项
/// </summary>
public sealed record WebViewOptions
{
    /// <summary>
    /// 页面打开新窗口时交给系统浏览器（仅 http、https）；为 false 时拒绝
    /// </summary>
    public bool OpenNewWindowInBrowser { get; init; } = true;

    /// <summary>
    /// 页面下载的保存目录；为空时拒绝页面发起的下载
    /// </summary>
    public string? DownloadFolder { get; init; }

    /// <summary>
    /// 网页进程异常退出且自动恢复无效时调用（在界面线程），由界面显示说明和重新加载入口。
    /// 参数为退出类型：浏览器进程退出后无法重新加载，只能重新打开页面所在窗口
    /// </summary>
    public Action<CoreWebView2ProcessFailedKind>? ProcessCrashed { get; init; }
}
