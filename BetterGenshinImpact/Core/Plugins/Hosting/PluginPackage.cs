namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 检查过的插件安装包（.zip 或 .nupkg），确认后交给 <see cref="PluginManager.InstallAsync"/> 安装。
/// </summary>
/// <param name="FilePath">安装包路径。</param>
/// <param name="Root">插件文件在压缩包中的根目录前缀，如 ""、"plugin/"，分隔符为 /。</param>
/// <param name="Manifest">插件清单。</param>
/// <param name="CompatibilityError">不兼容的原因；兼容时为 null。</param>
/// <param name="FileCount">插件文件数。</param>
/// <param name="TotalBytes">解压后的总大小。</param>
public sealed record PluginPackage(
    string FilePath,
    string Root,
    PluginManifest Manifest,
    string? CompatibilityError,
    int FileCount,
    long TotalBytes);

/// <summary>
/// 安装结果。
/// </summary>
/// <param name="Entry">安装后的插件条目。</param>
/// <param name="RequiresRestart">同标识的插件已加载，新版本在下次启动时生效。</param>
public sealed record PluginInstallResult(PluginEntry Entry, bool RequiresRestart);
