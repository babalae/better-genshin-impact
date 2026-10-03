using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按需读取资源详情；索引阶段不调用本服务，避免解析不可见资源内容。
/// </summary>
public static class PuloniaTaskResourcePreviewLoader
{
    /// <summary>
    /// JS README 预览最多读取的字符数。
    /// </summary>
    private const int MaxReadmePreviewCharacters = 16000;

    /// <summary>
    /// 加载一项资源的轻量详情；JS 额外读取清单和有限 README。
    /// </summary>
    public static async Task<PuloniaTaskResourcePreview> LoadAsync(PuloniaTaskResourceDescriptor resource,
        string? taskType, CancellationToken ct = default)
    {
        var header = $"相对路径：{resource.RelativePath}\n最后修改：{resource.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
        if (resource.IsDirectory && taskType is "pathing" or "keymouse")
            header += $"\n包含资源：{resource.ChildResourceCount} 项";
        else if (!resource.IsDirectory)
            header += $"\n文件大小：{FormatFileSize(resource.Size)}";
        if (taskType != "javascript")
            return new PuloniaTaskResourcePreview(header, null);

        var manifestPath = Path.Combine(resource.FullPath, "manifest.json");
        var manifest = Manifest.FromJson(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false));
        var authors = manifest.Authors.Count == 0
            ? "未注明"
            : string.Join("、", manifest.Authors.Select(author => author.Name));
        var builder = new StringBuilder();
        builder.AppendLine($"名称：{manifest.Name}");
        builder.AppendLine($"版本：{manifest.Version}");
        builder.AppendLine($"作者：{authors}");
        builder.AppendLine(header);
        if (!string.IsNullOrWhiteSpace(manifest.Description))
        {
            builder.AppendLine();
            builder.AppendLine(manifest.Description.Trim());
        }

        var readmePath = Path.Combine(resource.FullPath, "README.md");
        if (File.Exists(readmePath))
        {
            builder.AppendLine();
            builder.AppendLine("README.md");
            builder.AppendLine("──────────");
            builder.Append(await ReadTextPreviewAsync(readmePath, MaxReadmePreviewCharacters, ct)
                .ConfigureAwait(false));
        }
        return new PuloniaTaskResourcePreview(builder.ToString().TrimEnd(), manifest.Name);
    }

    /// <summary>
    /// 有限读取文本预览，避免超大 README 长期占用界面内存。
    /// </summary>
    private static async Task<string> ReadTextPreviewAsync(string path, int maxCharacters, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[maxCharacters + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
        return read > maxCharacters
            ? new string(buffer, 0, maxCharacters) + "\n\n……README 预览已截断"
            : new string(buffer, 0, read);
    }

    /// <summary>
    /// 格式化文件大小供资源详情展示。
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:0.##} KB";
        return $"{bytes / 1024d / 1024d:0.##} MB";
    }
}
