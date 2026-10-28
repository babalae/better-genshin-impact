using System;
using System.Collections.Generic;
using System.IO;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// 普通环境中的 Profile，用于在同一环境内隔离 Cookie 和网页存储
/// </summary>
/// <param name="Environment">所属环境，必须在 <see cref="WebViewEnvironments.All"/> 中</param>
/// <param name="ProfileName">Profile 名称；Default 表示环境的默认 Profile</param>
/// <param name="DisplayName">存储空间窗口中的名称</param>
public sealed record WebViewProfile(WebViewEnvironmentDefinition Environment, string ProfileName, string DisplayName)
{
    /// <summary>
    /// 默认 Profile 的名称，WebView2 不需要另外指定
    /// </summary>
    public const string DefaultProfileName = "Default";

    /// <summary>
    /// 存储空间窗口中的说明：这里存的是什么
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// 清理时是否清除该 Profile 的全部数据（含网页保存的设置），需要用户确认；为 false 时只清理缓存
    /// </summary>
    public bool ClearsAllData { get; init; }

    /// <summary>
    /// 是否为环境的默认 Profile
    /// </summary>
    public bool IsDefault => string.Equals(ProfileName, DefaultProfileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Profile 的数据目录。默认 Profile 为 EBWebView/Default，命名 Profile 为 EBWebView/WV2Profile_{小写名称}
    /// （已用 CoreWebView2Profile.ProfilePath 实测确认）
    /// </summary>
    public string ProfileFolder => Path.Combine(Environment.DataFolder, "EBWebView",
        IsDefault ? DefaultProfileName : "WV2Profile_" + ProfileName.ToLowerInvariant());
}

/// <summary>
/// 已定义的 Profile。需要独立登录态或存储的新功能在这里新增一项并放进 <see cref="All"/>
/// </summary>
public static class WebViewProfiles
{
    /// <summary>
    /// 软件内网页：更新日志、脚本仓库、地图路径编辑器
    /// </summary>
    public static readonly WebViewProfile Default = new(WebViewEnvironments.App, WebViewProfile.DefaultProfileName, "软件内网页 · 网页缓存")
    {
        Description = "更新日志、脚本仓库、地图编辑器等网页的缓存，保留网页登录与设置",
    };

    /// <summary>
    /// 脚本 HTML 遮罩，与其他网页隔离
    /// </summary>
    public static readonly WebViewProfile HtmlMask = new(WebViewEnvironments.App, "HtmlMask", "软件内网页 · 脚本遮罩数据")
    {
        Description = "脚本 HTML 遮罩网页的缓存和网页数据",
        ClearsAllData = true,
    };

    /// <summary>
    /// 全部 Profile，供存储区域登记
    /// </summary>
    public static IReadOnlyList<WebViewProfile> All { get; } = [Default, HtmlMask];
}
