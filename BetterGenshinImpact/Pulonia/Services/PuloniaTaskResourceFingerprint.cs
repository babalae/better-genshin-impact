using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 统一计算文件、JS 项目和目录引用的稳定 SHA-256 资源指纹。
/// 本地资源、仓库固定快照、创建任务、运行准备、更新检查和续跑都只调用这里；
/// 输出与已保存计划中的 resource_version 逐字节一致，调整实现时不能改变结果。
/// </summary>
public static class PuloniaTaskResourceFingerprint
{
    /// <summary>JS 版本只由根目录这两个文件决定；顺序固定为相对路径的稳定排序。</summary>
    private static readonly string[] _javaScriptResourceFileNames = ["main.js", "manifest.json"];

    /// <summary>仓库目录引用参与指纹的资源数量上限，超过时拒绝生成部分清单的指纹。</summary>
    private const int MaxRepositoryDirectoryResourceCount = 10000;

    /// <summary>计算原始内容的指纹：SHA-256 的小写十六进制。</summary>
    public static string ComputeContentVersion(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// 对规范化清单文本计算稳定指纹。
    /// </summary>
    public static string ComputeTextVersion(string value)
        => ComputeContentVersion(Encoding.UTF8.GetBytes(value));

    /// <summary>
    /// 计算清单指纹：每项写成“相对路径\0指纹\n”后整体求指纹。
    /// JS 项目、目录引用和运行准备中的目录分组共用这一格式，调用方负责传入稳定顺序。
    /// </summary>
    /// <param name="entries">按稳定顺序排列的相对路径与对应指纹</param>
    public static string ComputeManifestVersion(IEnumerable<(string RelativePath, string? Version)> entries)
    {
        var manifest = new StringBuilder();
        foreach (var (relativePath, version) in entries)
            manifest.Append(relativePath).Append('\0').Append(version).Append('\n');
        return ComputeTextVersion(manifest.ToString());
    }

    /// <summary>只计算根目录 manifest.json 与 main.js 的内容指纹；不扫描或读取任何其他文件。</summary>
    public static Task<string> ComputeJavaScriptVersionAsync(string directory, CancellationToken ct = default)
        => Task.Run(() => ComputeDirectoryVersionAsync(directory, GetJavaScriptResourceFiles(directory, ct), ct), ct);

    /// <summary>定位 JS 唯一的两个版本文件，保留重解析点保护；缺失文件不能生成部分或空版本。</summary>
    public static IReadOnlyList<string> GetJavaScriptResourceFiles(string directory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(directory);
        var attributes = File.GetAttributes(root);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("JS 项目必须是普通目录，不能直接指向文件或重解析点。");

        // 直接读取必需文件，其他脚本、assets 和运行数据的变化或读取失败均不参与版本判定。
        var files = new string[_javaScriptResourceFileNames.Length];
        for (var index = 0; index < files.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.Combine(root, _javaScriptResourceFileNames[index]);
            var fileAttributes = File.GetAttributes(file);
            if ((fileAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException($"JS 版本文件 {_javaScriptResourceFileNames[index]} 必须是普通文件。");
            files[index] = file;
        }
        return files;
    }

    /// <summary>
    /// 计算单个文件的内容指纹。
    /// </summary>
    public static async Task<string> ComputeFileVersionAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false))
            .ToLowerInvariant();
    }

    /// <summary>
    /// 按稳定相对路径和文件内容计算目录指纹。
    /// </summary>
    public static async Task<string> ComputeDirectoryVersionAsync(string directory,
        IEnumerable<string> orderedFiles, CancellationToken ct = default)
    {
        var entries = new List<(string RelativePath, string? Version)>();
        foreach (var file in orderedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var hash = await ComputeFileVersionAsync(file, ct).ConfigureAwait(false);
            entries.Add((NormalizeRelativePath(directory, file), hash));
        }
        return ComputeManifestVersion(entries);
    }

    /// <summary>
    /// 在固定版本的仓库快照上计算资源指纹，算法与本地资源相同：
    /// JS 只看 main.js 和 manifest.json，单文件为正文指纹，目录为 JSON 资源清单指纹。
    /// </summary>
    /// <param name="snapshot">固定版本的读取会话</param>
    /// <param name="relativePath">相对仓库 repo/ 内容目录的资源路径</param>
    /// <param name="taskType">资源能力类型</param>
    /// <param name="recursive">目录引用是否包含子目录</param>
    /// <param name="directory">是否为目录引用</param>
    public static string ComputeRepositoryVersion(ScriptRepositorySnapshot snapshot, string relativePath, string taskType,
        bool recursive = true, bool directory = false)
    {
        relativePath = ScriptRepositorySnapshot.NormalizePath(relativePath);
        var root = "repo/" + relativePath;
        if (taskType == "javascript")
            return ComputeManifestVersion(_javaScriptResourceFileNames.Select(name =>
                (name, (string?)ComputeContentVersion(snapshot.ReadBytes(root + "/" + name)))));
        if (!directory) return ComputeContentVersion(snapshot.ReadBytes(root));
        var files = ScriptRepositoryStore.ListDirectoryResources(snapshot, relativePath, recursive);
        if (files.Count == 0 || files.Count > MaxRepositoryDirectoryResourceCount)
            throw new IOException("目录中没有资源或资源数量超过安全上限。");
        return ComputeManifestVersion(files.Select(file =>
            (file, (string?)ComputeContentVersion(snapshot.ReadBytes(root + "/" + file)))));
    }

    /// <summary>
    /// 统一相对路径分隔符，确保指纹不依赖 Windows 路径展示格式。
    /// </summary>
    public static string NormalizeRelativePath(string directory, string file)
        => Path.GetRelativePath(directory, file).Replace('\\', '/');
}
