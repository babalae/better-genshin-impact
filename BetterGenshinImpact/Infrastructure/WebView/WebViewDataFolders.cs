using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// WebView2 数据目录的布局：云原神实例目录，以及 Profile 目录下可以安全删除的缓存子目录。
/// 登录状态（Network/Cookies）和网页存储（Local Storage、IndexedDB）不在其中
/// </summary>
public static class WebViewDataFolders
{
    /// <summary>
    /// Profile 目录下的缓存子目录：网页缓存、脚本编译缓存和 GPU 着色器缓存（按实际目录结构确认）
    /// </summary>
    private static readonly string[] ProfileCacheFolderNames =
        ["Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache"];

    /// <summary>
    /// Profile 目录下的缓存子目录完整路径
    /// </summary>
    public static IReadOnlyList<string> GetCacheFolders(string profileFolder) =>
        ProfileCacheFolderNames.Select(name => Path.Combine(profileFolder, name)).ToList();

    /// <summary>
    /// 云原神实例的用户数据目录
    /// </summary>
    public static string GetCloudGameFolder(string instanceName) =>
        Path.Combine(AppPaths.WebViewCloudGame, instanceName.Trim());

    /// <summary>
    /// 云原神实例的默认 Profile 目录，缓存和登录状态都在这里
    /// </summary>
    public static string GetCloudGameProfileFolder(string instanceName) =>
        Path.Combine(GetCloudGameFolder(instanceName), "EBWebView", WebViewProfile.DefaultProfileName);
}
