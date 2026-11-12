using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.Core.Script;

public partial class ScriptRepoUpdater
{
    /// <summary>
    /// 公共资源索引中的文件路径、字节数和摘要。
    /// </summary>
    private sealed record AssetEntry(string Path, int Size, string Sha256);

    /// <summary>
    /// 读取仓库根目录的公共资源索引。
    /// </summary>
    internal string GetAssetIndexJson() => ReadAssetSnapshot(read =>
    {
        var bytes = read("asset.json");
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }) ?? string.Empty;

    /// <summary>
    /// 读取资源索引中指定的文件，路径相对于 assets/。
    /// </summary>
    internal byte[]? ReadAssetFile(string relPath) => ReadAssetSnapshot(read =>
    {
        var entry = ReadAssetEntries(read)?.Find(file =>
            string.Equals(file.Path, relPath, StringComparison.OrdinalIgnoreCase));
        return entry == null ? null : ReadIndexedAsset(read, entry);
    });

    /// <summary>
    /// 从同一个仓库快照按索引读取指定类型的资源。
    /// </summary>
    internal List<(string Path, byte[] Content)>? ReadAssetFiles(string directory, string extension)
        => ReadAssetSnapshot(read =>
        {
            var entries = ReadAssetEntries(read);
            if (entries == null)
            {
                return null;
            }

            var prefix = directory + "/";
            var files = new List<(string Path, byte[] Content)>();
            foreach (var entry in entries)
            {
                if (!entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !entry.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bytes = ReadIndexedAsset(read, entry);
                if (bytes == null)
                {
                    // 不用不完整的快照更新用户曲库。
                    return null;
                }
                files.Add((entry.Path, bytes));
            }

            return files;
        });

    /// <summary>
    /// 解析资源索引中的逐文件记录。
    /// </summary>
    private static List<AssetEntry>? ReadAssetEntries(Func<string, byte[]?> read)
    {
        var bytes = read("asset.json");
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        return JObject.Parse(Encoding.UTF8.GetString(bytes))["files"] is JArray files
            ? files.ToObject<List<AssetEntry>>()
            : null;
    }

    /// <summary>
    /// 读取资源并核对索引记录的大小与摘要。
    /// </summary>
    private static byte[]? ReadIndexedAsset(Func<string, byte[]?> read, AssetEntry entry)
    {
        if (entry.Size is < 0 or > 2 * 1024 * 1024 || entry.Sha256?.Length != 64)
        {
            return null;
        }

        var bytes = read($"assets/{entry.Path}");
        return bytes != null && bytes.Length == entry.Size
            && string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), entry.Sha256, StringComparison.OrdinalIgnoreCase)
            ? bytes
            : null;
    }

    /// <summary>
    /// 让索引与文件读取共用同一个 Git 提交。
    /// </summary>
    private T? ReadAssetSnapshot<T>(Func<Func<string, byte[]?>, T?> read) where T : class
    {
        try
        {
            var repoPath = CenterRepoPath;
            if (!Directory.Exists(repoPath))
            {
                return null;
            }

            if (IsGitRepository(repoPath))
            {
                using var repo = new Repository(repoPath);
                var tree = repo.Head.Tip?.Tree;
                return tree == null ? null : read(path => ReadAssetSnapshotFile(repoPath, tree, path));
            }

            return read(path => ReadAssetSnapshotFile(repoPath, null, path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从中央仓库读取公共资源失败");
            return null;
        }
    }

    /// <summary>
    /// 从 Git 树或解压目录读取资源，拒绝路径逃逸及链接。
    /// </summary>
    private static byte[]? ReadAssetSnapshotFile(string repoPath, Tree? tree, string path)
    {
        var parts = path.Split('/');
        var invalidChars = Path.GetInvalidFileNameChars();
        if (parts.Any(part => part.Length == 0 || part.EndsWith('.') || part.EndsWith(' ')
                              || part.IndexOfAny(invalidChars) >= 0))
        {
            return null;
        }

        if (tree != null)
        {
            var entry = tree[path];
            if (entry is not { TargetType: TreeEntryTargetType.Blob } || entry.Mode == Mode.SymbolicLink)
            {
                return null;
            }

            using var content = ((Blob)entry.Target).GetContentStream();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            return buffer.ToArray();
        }

        var filePath = Path.Combine(repoPath, Path.Combine(parts));
        if (!File.Exists(filePath))
        {
            return null;
        }

        var current = repoPath;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }
        }

        return File.ReadAllBytes(filePath);
    }
}
