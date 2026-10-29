using System;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// 页面请求打开新窗口时的处理方式。无论哪种方式都不会在程序内弹出新的浏览器窗口
/// </summary>
public enum WebViewNewWindowPolicy
{
    /// <summary>
    /// 交给系统浏览器打开（仅 http、https）
    /// </summary>
    OpenInBrowser,

    /// <summary>
    /// 拒绝
    /// </summary>
    Reject,

    /// <summary>
    /// 在当前网页中打开（仅 http、https），用于悬浮小窗这类单页浏览场景
    /// </summary>
    NavigateInPlace,
}

/// <summary>
/// 普通网页的初始化选项
/// </summary>
public sealed record WebViewOptions
{
    /// <summary>
    /// 页面打开新窗口时的处理方式，默认交给系统浏览器
    /// </summary>
    public WebViewNewWindowPolicy NewWindowPolicy { get; init; } = WebViewNewWindowPolicy.OpenInBrowser;

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
