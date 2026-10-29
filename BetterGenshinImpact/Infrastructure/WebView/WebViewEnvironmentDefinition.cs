using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// 普通 WebView2 环境的定义：一个用户数据目录和它固定不变的配置（见设计文档 8.2 节）
/// </summary>
/// <param name="Id">环境标识，也是数据目录名，只使用字母、数字和短横线，不能是 CloudGame</param>
/// <param name="DisplayName">展示名称</param>
public sealed record WebViewEnvironmentDefinition(string Id, string DisplayName)
{
    /// <summary>
    /// 浏览器参数。必须是常量，不能读取用户配置：同一数据目录的所有进程参数必须一致，否则后创建的环境会初始化失败
    /// </summary>
    public string BrowserArguments { get; init; } = "";

    /// <summary>
    /// 用户数据目录，由 Id 决定，不允许单独指定
    /// </summary>
    public string DataFolder => Path.Combine(AppPaths.WebView, Id);
}

/// <summary>
/// 已定义的普通环境，这是查看和修改普通环境配置的唯一位置。新增环境只在这里加一项并放进 <see cref="All"/>
/// </summary>
public static class WebViewEnvironments
{
    /// <summary>
    /// 软件内网页的默认环境：更新日志、脚本仓库、地图路径编辑器、脚本遮罩
    /// </summary>
    public static readonly WebViewEnvironmentDefinition App = new("App", "软件内网页");

    /// <summary>
    /// 悬浮小窗：独立浏览器进程，视频解码或崩溃不影响其他软件内网页。
    /// 允许没有用户手势的媒体播放，快捷键播放、切集后续播不会被自动播放策略拦截
    /// </summary>
    public static readonly WebViewEnvironmentDefinition Guide = new("Guide", "悬浮小窗")
    {
        BrowserArguments = "--autoplay-policy=no-user-gesture-required",
    };

    /// <summary>
    /// 全部普通环境，供环境入口校验、存储区域登记和识别不再使用的目录
    /// </summary>
    public static IReadOnlyList<WebViewEnvironmentDefinition> All { get; } = [App, Guide];
}
