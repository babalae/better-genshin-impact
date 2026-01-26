using System;
using System.IO;
using System.Threading;

namespace BetterGenshinImpact.Core.Config;

internal static class UserCache
{
    private static readonly object InitLock = new();
    private static bool _initialized;
    private static FileSystemWatcher? _watcher;
    private static int _suppressCount;
    private static readonly string RootPath = Path.Combine(Path.GetTempPath(), "BetterGenshinImpact", "UserCache");

    public static string RootDirectory => RootPath;

    private static bool IsSuppressed => Volatile.Read(ref _suppressCount) > 0;

    private static void BeginSuppress()
    {
        Interlocked.Increment(ref _suppressCount);
    }

    private static void EndSuppress()
    {
        Interlocked.Decrement(ref _suppressCount);
    }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            UserStorage.Initialize();
            // 不再预缓存所有文件，改为按需缓存
            PrepareCache();
            StartWatcher();
            _initialized = true;
        }
    }

    public static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            SyncToDb();
        }
        catch
        {
            // Suppress cleanup errors.
        }

        StopWatcher();
        CleanupCache();
        _initialized = false;
    }

    public static string Absolute(string path)
    {
        Initialize();
        if (UserStorage.TryNormalizeUserPath(path, out var normalized))
        {
            return Path.Combine(RootPath, normalized);
        }

        return Path.Combine(RootPath, path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public static string? ReadTextIfExist(string path)
    {
        Initialize();
        var fullPath = Absolute(path);
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath);
        }

        if (UserStorage.TryReadText(path, out var content) && content != null)
        {
            WriteCacheFile(path, System.Text.Encoding.UTF8.GetBytes(content), UserStorage.GetLastWriteTimeUtc(path));
            return content;
        }

        return null;
    }

    public static byte[]? ReadBytesIfExist(string path)
    {
        Initialize();
        var fullPath = Absolute(path);
        if (File.Exists(fullPath))
        {
            return File.ReadAllBytes(fullPath);
        }

        if (UserStorage.TryReadBytes(path, out var content, out var updatedUtc) && content != null)
        {
            WriteCacheFile(path, content, updatedUtc);
            return content;
        }

        return null;
    }

    public static void WriteText(string path, string content)
    {
        Initialize();
        var updatedUtc = DateTimeOffset.UtcNow;
        UserStorage.TryWriteText(path, content, updatedUtc);
        WriteCacheFile(path, System.Text.Encoding.UTF8.GetBytes(content), updatedUtc);
    }

    public static void WriteBytes(string path, byte[] content, bool isText)
    {
        Initialize();
        var updatedUtc = DateTimeOffset.UtcNow;
        UserStorage.TryWriteBytes(path, content, isText, updatedUtc);
        WriteCacheFile(path, content, updatedUtc);
    }

    public static bool Delete(string path)
    {
        Initialize();
        UserStorage.Delete(path);
        var fullPath = Absolute(path);
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SyncToDb()
    {
        Initialize();
        BeginSuppress();
        try
        {
            foreach (var file in Directory.GetFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                UpsertFromCache(file);
            }
        }
        finally
        {
            EndSuppress();
        }
    }

    private static void PrepareCache()
    {
        BeginSuppress();
        try
        {
            // 清理旧缓存
            CleanupCache();
            // 只创建目录结构，不预缓存文件
            Directory.CreateDirectory(RootPath);
            EnsureSeedDirectories();
        }
        finally
        {
            EndSuppress();
        }
    }

    private static void RebuildCache()
    {
        BeginSuppress();
        try
        {
            CleanupCache();
            Directory.CreateDirectory(RootPath);
            EnsureSeedDirectories();

            foreach (var entry in UserStorage.ListEntries())
            {
                if (UserStorage.TryReadBytes(entry.Path, out var content, out var updatedUtc) && content != null)
                {
                    WriteCacheFile(entry.Path, content, updatedUtc);
                }
            }
        }
        finally
        {
            EndSuppress();
        }
    }

    private static void EnsureSeedDirectories()
    {
        // 只创建必要的目录，脚本目录不需要在缓存中创建
        var seedDirs = new[]
        {
            "Temp"  // 只保留临时目录
        };

        foreach (var dir in seedDirs)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, dir));
        }
    }

    private static void CleanupCache()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private static void StartWatcher()
    {
        StopWatcher();
        Directory.CreateDirectory(RootPath);
        _watcher = new FileSystemWatcher(RootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    private static void StopWatcher()
    {
        if (_watcher == null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnDeleted;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
        _watcher = null;
    }

    private static void OnChanged(object? sender, FileSystemEventArgs e)
    {
        if (IsSuppressed)
        {
            return;
        }

        if (Directory.Exists(e.FullPath))
        {
            return;
        }

        UpsertFromCache(e.FullPath);
    }

    private static void OnDeleted(object? sender, FileSystemEventArgs e)
    {
        if (IsSuppressed)
        {
            return;
        }

        if (!TryGetRelativePath(e.FullPath, out var relative))
        {
            return;
        }

        UserStorage.Delete(relative);
    }

    private static void OnRenamed(object? sender, RenamedEventArgs e)
    {
        if (IsSuppressed)
        {
            return;
        }

        if (TryGetRelativePath(e.OldFullPath, out var oldRelative))
        {
            UserStorage.Delete(oldRelative);
        }

        if (Directory.Exists(e.FullPath))
        {
            return;
        }

        UpsertFromCache(e.FullPath);
    }

    private static void UpsertFromCache(string fullPath)
    {
        if (!TryGetRelativePath(fullPath, out var relative))
        {
            return;
        }

        if (UserStorage.IsIgnoredPath(relative))
        {
            return;
        }

        var content = TryReadFileBytes(fullPath);
        if (content == null)
        {
            return;
        }

        var updatedUtc = File.GetLastWriteTimeUtc(fullPath);
        UserStorage.TryWriteBytes(relative, content, UserStorage.IsTextFile(fullPath), new DateTimeOffset(updatedUtc, TimeSpan.Zero));
    }

    private static bool TryGetRelativePath(string fullPath, out string relative)
    {
        relative = string.Empty;
        try
        {
            var rel = Path.GetRelativePath(RootPath, fullPath);
            if (rel.StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            relative = rel.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? TryReadFileBytes(string fullPath)
    {
        const int maxAttempts = 5;
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                return File.ReadAllBytes(fullPath);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static void WriteCacheFile(string path, byte[] content, DateTimeOffset? updatedUtc)
    {
        if (!UserStorage.TryNormalizeUserPath(path, out var normalized))
        {
            return;
        }

        var fullPath = Path.Combine(RootPath, normalized);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        BeginSuppress();
        try
        {
            File.WriteAllBytes(fullPath, content);
            if (updatedUtc.HasValue)
            {
                File.SetLastWriteTimeUtc(fullPath, updatedUtc.Value.UtcDateTime);
            }
        }
        finally
        {
            EndSuppress();
        }
    }
}
