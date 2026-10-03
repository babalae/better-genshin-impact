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
