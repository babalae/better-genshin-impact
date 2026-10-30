using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Core.Script.Repositories;

namespace BetterGenshinImpact.Core.Script.Settings;

/// <summary>
/// 脚本通过 manifest.settings_html 声明的 HTML 设置页。
/// 入口所在目录是页面网站的根目录，只有这个目录中扩展名在白名单内的文件会提供给页面。
/// </summary>
public sealed class ScriptSettingsHtmlPage
{
    /// <summary>单个文件的大小上限。</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;

    /// <summary>仓库资源设置页目录的文件数上限。</summary>
    public const int MaxFileCount = 500;

    /// <summary>仓库资源设置页目录的总大小上限。</summary>
    public const long MaxTotalBytes = 20L * 1024 * 1024;

    /// <summary>保留地址的目录名，页面目录中同名子目录的文件不会提供给页面。</summary>
    public const string ReservedDirectory = "__bgi";

    /// <summary>可以提供给页面的扩展名及其 Content-Type。</summary>
    internal static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".htm"] = "text/html; charset=utf-8",
            [".js"] = "text/javascript; charset=utf-8",
            [".mjs"] = "text/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".map"] = "application/json; charset=utf-8",
            [".txt"] = "text/plain; charset=utf-8",
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".ico"] = "image/x-icon",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".otf"] = "font/otf",
            [".wasm"] = "application/wasm",
            [".mp3"] = "audio/mpeg",
            [".ogg"] = "audio/ogg",
            [".wav"] = "audio/wav",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
        };

    /// <summary>设置页目录（入口所在目录）的完整路径。</summary>
    public string PageDirectory { get; }

    /// <summary>入口文件名，位于设置页目录下。</summary>
    public string EntryFile { get; }

    /// <summary>页面使用的虚拟主机名；同一项目每次打开相同，网页存储按它隔离。</summary>
    public string HostName { get; }

    /// <summary>清单中的脚本名称。</summary>
    public string ScriptName { get; }

    /// <summary>清单中的脚本版本。</summary>
    public string ScriptVersion { get; }

    /// <summary>入口地址。</summary>
    public Uri EntryUri => new($"https://{HostName}/{Uri.EscapeDataString(EntryFile)}");

    private ScriptSettingsHtmlPage(string pageDirectory, string entryFile, string hostName, string scriptName,
        string scriptVersion)
    {
        PageDirectory = pageDirectory;
        EntryFile = entryFile;
        HostName = hostName;
        ScriptName = scriptName;
        ScriptVersion = scriptVersion;
    }

    /// <summary>
    /// 按清单解析设置页，检查入口存在且路径上没有链接。
    /// </summary>
    /// <param name="projectDirectory">JS 项目目录；仓库资源为提取了设置页的预览目录。</param>
    /// <param name="manifest">项目清单，SettingsHtml 不能为空。</param>
    /// <param name="identity">决定主机名的项目身份；为空时使用项目目录。</param>
    /// <exception cref="ArgumentException">settings_html 路径不合法。</exception>
    /// <exception cref="IOException">入口不存在或路径上有链接。</exception>
    public static ScriptSettingsHtmlPage Resolve(string projectDirectory, Manifest manifest, string? identity = null)
    {
        var root = Path.GetFullPath(projectDirectory);
        var entry = NormalizeEntry(manifest.SettingsHtml);
        if (HasReparsePoint(root, entry))
            throw new IOException("设置页路径不能包含链接：" + manifest.SettingsHtml);
        if (!File.Exists(Path.Combine(root, entry)))
            throw new FileNotFoundException("manifest 指定的设置页不存在：" + manifest.SettingsHtml);

        var slash = entry.LastIndexOf('/');
        var directory = slash < 0 ? root : Path.Combine(root, entry[..slash]);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes((identity ?? "local:" + root).ToUpperInvariant()));
        var hostName = $"ss-{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}.bettergi.local";
        return new ScriptSettingsHtmlPage(directory, entry[(slash + 1)..], hostName, manifest.Name, manifest.Version);
    }

    /// <summary>
    /// 规范 settings_html：项目内的相对路径，不含 ..，扩展名为 .html 或 .htm。
    /// </summary>
    /// <returns>以 / 分隔的相对路径。</returns>
    /// <exception cref="ArgumentException">路径不合法。</exception>
    public static string NormalizeEntry(string settingsHtml)
    {
        try
        {
            var entry = ScriptRepositorySnapshot.NormalizePath(settingsHtml);
            if (entry.Length > 0 && Path.GetExtension(entry).ToLowerInvariant() is ".html" or ".htm")
                return entry;
        }
        catch (ArgumentException)
        {
            // 统一报告下方的错误
        }

        throw new ArgumentException("settings_html 必须是项目内 .html 或 .htm 文件的相对路径：" + settingsHtml);
    }

    /// <summary>
    /// 检查仓库资源设置页目录的文件数与大小；本地目录直接读取，不受此限制。
    /// </summary>
    /// <param name="fileSizes">目录中每个文件的字节数。</param>
    /// <exception cref="IOException">超出上限。</exception>
    public static void CheckLimits(IReadOnlyCollection<long> fileSizes)
    {
        if (fileSizes.Count > MaxFileCount)
            throw new IOException($"设置页目录最多包含 {MaxFileCount} 个文件，当前 {fileSizes.Count} 个。");
        if (fileSizes.Any(size => size > MaxFileBytes))
            throw new IOException("设置页目录中的单个文件不能超过 10 MB。");
        if (fileSizes.Sum() > MaxTotalBytes)
            throw new IOException("设置页目录总大小不能超过 20 MB。");
    }

    /// <summary>
    /// 把页面请求的地址路径解析为设置页目录中的文件。
    /// 越界、位于保留目录、扩展名不在白名单、路径上有链接或超过大小上限时返回 null。
    /// </summary>
    /// <param name="urlPath">已解码的地址路径，例如 /css/style.css；/ 表示入口。</param>
    /// <returns>文件完整路径。</returns>
    public string? ResolveFile(string urlPath)
    {
        string relative;
        try
        {
            var path = urlPath.TrimStart('/');
            relative = ScriptRepositorySnapshot.NormalizePath(path.Length == 0 ? EntryFile : path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (relative.Split('/')[0].Equals(ReservedDirectory, StringComparison.OrdinalIgnoreCase)
            || !ContentTypes.ContainsKey(Path.GetExtension(relative))
            || HasReparsePoint(PageDirectory, relative))
            return null;

        var file = new FileInfo(Path.Combine(PageDirectory, relative));
        return file.Exists && file.Length <= MaxFileBytes ? file.FullName : null;
    }

    /// <summary>
    /// 逐级检查相对路径上的目录和文件是否为链接，防止经链接读到目录以外的内容。
    /// </summary>
    private static bool HasReparsePoint(string root, string relative)
    {
        var path = root;
        foreach (var segment in relative.Split('/'))
        {
            path = Path.Combine(path, segment);
            if (Path.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                return true;
        }

        return false;
    }
}
