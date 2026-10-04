using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 统一计算文件、JS 项目和目录引用的稳定 SHA-256 资源指纹。
/// </summary>
public static class PuloniaTaskResourceFingerprint
{
    /// <summary>JS 版本只由根目录这两个文件决定；顺序固定为相对路径的稳定排序。</summary>
    private static readonly string[] _javaScriptResourceFileNames = ["main.js", "manifest.json"];

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
        var manifest = new StringBuilder();
        foreach (var file in orderedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var hash = await ComputeFileVersionAsync(file, ct).ConfigureAwait(false);
            manifest.Append(NormalizeRelativePath(directory, file)).Append('\0').Append(hash).Append('\n');
        }
        return ComputeTextVersion(manifest.ToString());
    }

    /// <summary>
    /// 对规范化清单文本计算稳定指纹。
    /// </summary>
    public static string ComputeTextVersion(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>
    /// 统一相对路径分隔符，确保指纹不依赖 Windows 路径展示格式。
    /// </summary>
    public static string NormalizeRelativePath(string directory, string file)
        => Path.GetRelativePath(directory, file).Replace('\\', '/');
}
