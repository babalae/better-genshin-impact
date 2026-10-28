using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 存储清理用的文件操作：统计大小、尽力删除。
/// 被占用或无权限的文件跳过并计数，不抛 IO 异常；不进入符号链接和目录联接，只删除链接本身。
/// </summary>
internal static class StorageFileOperations
{
    /// <summary>
    /// 统计文件或目录占用的字节数；不存在时为 0，无法访问的部分忽略
    /// </summary>
    public static long Measure(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            return TryGetLength(new FileInfo(path));
        }

        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(path));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in TryGetEntries(pending.Pop()) ?? [])
            {
                // 链接指向的内容不属于这块区域
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (entry is DirectoryInfo subDirectory)
                {
                    pending.Push(subDirectory);
                }
                else if (entry is FileInfo file)
                {
                    total += TryGetLength(file);
                }
            }
        }

        return total;
    }

    /// <summary>
    /// 清空区域路径：文件直接删除，目录删除其中的全部内容但保留目录本身
    /// </summary>
    public static StorageCleanupResult Clear(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            return DeleteFile(new FileInfo(path));
        }

        return Directory.Exists(path)
            ? DeleteContents(new DirectoryInfo(path), ct)
            : StorageCleanupResult.Empty;
    }

    /// <summary>
    /// 删除文件或整个目录（含只读文件）；不存在时视为成功
    /// </summary>
    public static StorageCleanupResult DeletePath(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            return DeleteFile(new FileInfo(path));
        }

        return Directory.Exists(path)
            ? DeleteDirectory(new DirectoryInfo(path), ct)
            : StorageCleanupResult.Empty;
    }

    /// <summary>
    /// 删除目录及其内容；有内容删除失败时保留目录
    /// </summary>
    private static StorageCleanupResult DeleteDirectory(DirectoryInfo directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 链接只删除自身，不进入目标目录
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return TryDelete(directory.Delete) ? StorageCleanupResult.Empty : new StorageCleanupResult(0, 1, false);
        }

        var result = DeleteContents(directory, ct);
        if (result.SkippedFiles > 0)
        {
            return result;
        }

        var deleted = TryDelete(() =>
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directory.Attributes &= ~FileAttributes.ReadOnly;
            }

            directory.Delete();
        });
        return deleted ? result : result with { SkippedFiles = 1 };
    }

    /// <summary>
    /// 删除目录中的全部内容
    /// </summary>
    private static StorageCleanupResult DeleteContents(DirectoryInfo directory, CancellationToken ct)
    {
        var entries = TryGetEntries(directory);
        if (entries is null)
        {
            return new StorageCleanupResult(0, 1, false);
        }

        var result = StorageCleanupResult.Empty;
        foreach (var entry in entries)
        {
            result = result.Add(entry is DirectoryInfo subDirectory
                ? DeleteDirectory(subDirectory, ct)
                : DeleteFile((FileInfo)entry));
        }

        return result;
    }

    /// <summary>
    /// 删除单个文件，去掉只读属性后再删除
    /// </summary>
    private static StorageCleanupResult DeleteFile(FileInfo file)
    {
        var length = TryGetLength(file);
        var deleted = TryDelete(() =>
        {
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
            }

            file.Delete();
        });
        return deleted ? new StorageCleanupResult(length, 0, false) : new StorageCleanupResult(0, 1, false);
    }

    /// <summary>
    /// 执行删除；目标已不存在视为成功，被占用或无权限视为失败
    /// </summary>
    private static bool TryDelete(Action delete)
    {
        try
        {
            delete();
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 列出目录内容；无法访问时返回 null
    /// </summary>
    private static FileSystemInfo[]? TryGetEntries(DirectoryInfo directory)
    {
        try
        {
            return directory.GetFileSystemInfos();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 读取文件长度；文件已消失或无法访问时为 0
    /// </summary>
    private static long TryGetLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
