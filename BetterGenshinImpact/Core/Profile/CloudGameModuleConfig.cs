namespace BetterGenshinImpact.Core.Profile;

/// <summary>
/// Profile 下的云原神模块配置。
/// WebView2 登录数据保存在同一 Profile 的 WebView2Data 目录，本期仍共享全局 User/config.json。
/// </summary>
public sealed class CloudGameModuleConfig
{
    /// <summary>
    /// 官方网页版云原神默认地址。
    /// </summary>
    public const string DefaultUrl = "https://ys.mihoyo.com/cloud/#/";

    /// <summary>
    /// 当前 Profile 使用的云原神页面地址。
    /// </summary>
    public string Url { get; set; } = DefaultUrl;

    /// <summary>
    /// 最近一次选择的父窗口显示模式。
    /// </summary>
    public string DisplayMode { get; set; } = "Interactive";

    /// <summary>
    /// 最近一次选择的脚本组名称。
    /// </summary>
    public string LastTask { get; set; } = string.Empty;
}
