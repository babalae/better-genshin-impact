using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 读取和解压插件安装包。zip 的 plugin.json 位于根目录或唯一的顶层目录；nupkg 的插件文件位于 plugin/ 目录。
/// 安装包中的路径一律校验，拒绝解压到目标目录之外。
/// </summary>
internal static class PluginPackageReader
{
    /// <summary>
    /// 解压后的总大小上限。
    /// </summary>
    private const long MaxTotalBytes = 1L << 30;

    /// <summary>
    /// 清单文件名。
    /// </summary>
    private const string ManifestFileName = "plugin.json";

    /// <summary>
    /// 检查安装包并读取清单。
    /// </summary>
    /// <param name="filePath">安装包路径。</param>
    /// <exception cref="InvalidDataException">不是插件安装包，或包内路径不安全、超过大小上限。</exception>
    public static PluginPackage Read(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var entries = Index(archive);
        var root = FindRoot(entries, filePath);

        // 校验全部插件文件的路径和总大小
        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var (name, entry) in Files(entries, root))
        {
            var relative = name[root.Length..];
            if (!SafePath.IsSafeRelativePath(relative))
            {
                throw new InvalidDataException($"安装包中的路径“{name}”不安全。");
            }

            totalBytes += entry.Length;
            if (totalBytes > MaxTotalBytes)
            {
                throw new InvalidDataException("安装包解压后超过 1 GB。");
            }

            relativePaths.Add(relative);
        }

        PluginManifest manifest;
        try
        {
            using var stream = entries[root + ManifestFileName].Open();
            manifest = JsonSerializer.Deserialize<PluginManifest>(stream, Global.ManifestJsonOptions)
                       ?? throw new InvalidDataException("插件清单为空。");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"插件清单格式错误：{e.Message}", e);
        }

        var error = PluginCompatibility.Check(manifest, path => relativePaths.Contains(Normalize(path)));
        return new PluginPackage(filePath, root, manifest, error, relativePaths.Count, totalBytes);
    }

    /// <summary>
    /// 把安装包中的插件文件解压到目标目录。
    /// </summary>
    /// <param name="package">检查过的安装包。</param>
    /// <param name="targetDirectory">目标目录，不存在时创建。</param>
    public static void Extract(PluginPackage package, string targetDirectory)
    {
        using var archive = ZipFile.OpenRead(package.FilePath);
        Directory.CreateDirectory(targetDirectory);
        foreach (var (name, entry) in Files(Index(archive), package.Root))
        {
            // SafePath.Combine 再次校验，防止安装包在检查后被替换
            var destination = SafePath.Combine(targetDirectory, name[package.Root.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    /// <summary>
    /// 按统一使用 / 分隔的名称索引压缩包条目。
    /// </summary>
    private static Dictionary<string, ZipArchiveEntry> Index(ZipArchive archive)
    {
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            result[Normalize(entry.FullName)] = entry;
        }

        return result;
    }

    /// <summary>
    /// 找到插件文件的根目录前缀。
    /// </summary>
    private static string FindRoot(Dictionary<string, ZipArchiveEntry> entries, string filePath)
    {
        if (filePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            return entries.ContainsKey("plugin/" + ManifestFileName)
                ? "plugin/"
                : throw new InvalidDataException("NuGet 包中没有 plugin/plugin.json，不是 BetterGI 插件。");
        }

        if (entries.ContainsKey(ManifestFileName))
        {
            return string.Empty;
        }

        var topDirectories = entries.Keys
            .Where(name => name.Contains('/'))
            .Select(name => name[..(name.IndexOf('/') + 1)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (topDirectories.Count == 1 && entries.ContainsKey(topDirectories[0] + ManifestFileName))
        {
            return topDirectories[0];
        }

        throw new InvalidDataException("安装包中没有 plugin.json，不是 BetterGI 插件。");
    }

    /// <summary>
    /// 根目录前缀下的文件条目（跳过目录条目）。
    /// </summary>
    private static IEnumerable<(string Name, ZipArchiveEntry Entry)> Files(Dictionary<string, ZipArchiveEntry> entries,
        string root)
        => entries
            .Where(kv => kv.Key.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !kv.Key.EndsWith('/'))
            .Select(kv => (kv.Key, kv.Value));

    /// <summary>
    /// 统一使用 / 作为分隔符。
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/');
}
