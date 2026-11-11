using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 插件清单 plugin.json。使用 Global.ManifestJsonOptions（snake_case）读取，未知字段忽略。
/// 字段规则见 Docs/design/plugin-system.md 第 9.2 节。
/// </summary>
public sealed class PluginManifest
{
    /// <summary>
    /// 清单格式版本，目前为 1。
    /// </summary>
    public int ManifestVersion { get; set; }

    /// <summary>
    /// 插件标识：小写字母开头，只含小写字母、数字、下划线，最长 32 个字符。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 插件版本，Semver 格式。
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 作者列表。
    /// </summary>
    public List<string> Authors { get; set; } = [];

    /// <summary>
    /// 插件说明。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 入口程序集，插件目录内的相对路径。
    /// </summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>
    /// 编译时使用的 BetterGI 版本，同时是插件支持的最低 BetterGI 版本。
    /// </summary>
    public string SdkVersion { get; set; } = string.Empty;

    /// <summary>
    /// 支持的最高 BetterGI 版本，空字符串表示不限。
    /// </summary>
    public string MaxHostVersion { get; set; } = string.Empty;

    /// <summary>
    /// 对应的 NuGet 包标识，用于以后检查更新，可为空。
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// 插件主页，可为空。
    /// </summary>
    public string Homepage { get; set; } = string.Empty;
}
