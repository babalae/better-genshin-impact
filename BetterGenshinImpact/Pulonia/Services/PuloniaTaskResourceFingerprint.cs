using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>JS 项目根目录下的常见运行数据目录；assets 内同名目录仍属于静态资源。</summary>
    private static readonly HashSet<string> _javaScriptRuntimeDirectories = new(StringComparer.OrdinalIgnoreCase)
        { "records", "CDInfo", "logs", "cache", "temp", ".git" };

    /// <summary>计算 JS 静态资源指纹；排除常见运行记录，但任何位置的可执行脚本仍参与校验。</summary>
    public static Task<string> ComputeJavaScriptVersionAsync(string directory,
        IEnumerable<string> orderedFiles, CancellationToken ct = default)
        => ComputeDirectoryVersionAsync(directory, orderedFiles.Where(file => IsJavaScriptResourceFile(directory, file)), ct);

    /// <summary>判断文件是否属于 JS 静态资源，避免采集记录和 CD 信息导致每次运行后误报更新。</summary>
    public static bool IsJavaScriptResourceFile(string directory, string file)
    {
        var relative = NormalizeRelativePath(directory, file);
        var extension = Path.GetExtension(relative);
        if (extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase))
            return true;
        var separator = relative.IndexOf('/');
        return separator < 0 || !_javaScriptRuntimeDirectories.Contains(relative[..separator]);
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
