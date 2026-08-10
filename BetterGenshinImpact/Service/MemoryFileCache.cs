using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Security;
using LazyCache;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

public sealed class MemoryFileCache
{
    private readonly IAppCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemoryFileCache> _logger;
    public static readonly string CacheRootDirectory = Global.Absolute(Path.Combine("User", "Cache", "MemoryFileCache"));

    public MemoryFileCache(
        IAppCache memoryCache,
        TimeProvider timeProvider,
        ILogger<MemoryFileCache> logger)
    {
        _memoryCache = memoryCache;
        _timeProvider = timeProvider;
        _logger = logger;
        Directory.CreateDirectory(CacheRootDirectory);
    }

    public Task<T?> GetOrAddAsync<T>(
        string cacheType,
        string cacheKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> factory,
        Func<T, byte[]> serialize,
        Func<byte[], T?> deserialize,
        CancellationToken ct = default)
    {
        return GetOrAddCoreAsync(
            CacheRootDirectory,
            cacheType,
            cacheKey,
            ttl,
            factory,
            serialize,
            deserialize,
            ct);
    }

    public Task<T?> GetOrAddInDirectoryAsync<T>(
        string cacheRootDirectory,
        string cacheType,
        string cacheKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> factory,
        Func<T, byte[]> serialize,
        Func<byte[], T?> deserialize,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cacheRootDirectory))
        {
            throw new ArgumentException("cacheRootDirectory cannot be empty.", nameof(cacheRootDirectory));
        }

        return GetOrAddCoreAsync(
            Path.GetFullPath(cacheRootDirectory),
            cacheType,
            cacheKey,
            ttl,
            factory,
            serialize,
            deserialize,
            ct);
    }

    private Task<T?> GetOrAddCoreAsync<T>(
        string cacheRootDirectory,
        string cacheType,
        string cacheKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> factory,
        Func<T, byte[]> serialize,
        Func<byte[], T?> deserialize,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("cacheKey cannot be empty.", nameof(cacheKey));
        }

        var normalizedCacheType = NormalizeCacheType(cacheType);
        var memoryCacheKey = $"{cacheRootDirectory}:{normalizedCacheType}:{cacheKey}";
        Directory.CreateDirectory(GetCacheTypeDirectory(cacheRootDirectory, normalizedCacheType));

        return _memoryCache.GetOrAddAsync<T?>(
                memoryCacheKey,
                async entry =>
                {
                    if (TryReadPayload(cacheRootDirectory, normalizedCacheType, cacheKey, ttl, out var payload, out var remaining))
                    {
                        entry.AbsoluteExpirationRelativeToNow = remaining;
                        try
                        {
                            return deserialize(payload);
                        }
                        catch
                        {
                            TryDeletePayload(cacheRootDirectory, normalizedCacheType, cacheKey);
                        }
                    }

                    entry.AbsoluteExpirationRelativeToNow = ttl;
                    var obj = await factory(CancellationToken.None).ConfigureAwait(false);
                    if (obj == null)
                    {
                        return default;
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = serialize(obj);
                    }
                    catch
                    {
                        return obj;
                    }

                    if (bytes is { Length: > 0 })
                    {
                        TryWritePayload(cacheRootDirectory, normalizedCacheType, cacheKey, bytes);
                    }

                    return obj;
                })
            .WaitAsync(ct);
    }

    public Task<T?> GetOrAddAsync<T>(
        string cacheKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> factory,
        Func<T, byte[]> serialize,
        Func<byte[], T?> deserialize,
        CancellationToken ct = default)
    {
        return GetOrAddAsync(
            "default",
            cacheKey,
            ttl,
            factory,
            serialize,
            deserialize,
            ct);
    }

    private static string NormalizeCacheType(string? cacheType)
    {
        if (string.IsNullOrWhiteSpace(cacheType))
        {
            return "default";
        }

        cacheType = cacheType.Trim();

        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(cacheType.Length);
        for (var i = 0; i < cacheType.Length; i++)
        {
            var ch = cacheType[i];
            if (ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar)
            {
                sb.Append('_');
                continue;
            }

            var isInvalid = false;
            for (var j = 0; j < invalidChars.Length; j++)
            {
                if (ch == invalidChars[j])
                {
                    isInvalid = true;
                    break;
                }
            }

            sb.Append(isInvalid ? '_' : ch);
        }

        var normalized = sb.ToString().Trim();
        if (normalized.Length == 0 || normalized is "." or "..")
        {
            return "default";
        }

        return normalized;
    }

    private static string GetCacheTypeDirectory(string cacheRootDirectory, string cacheType)
    {
        return Path.Combine(cacheRootDirectory, cacheType);
    }

    public void PurgeCacheTypeByCacheKeys(string cacheType, IReadOnlyCollection<string> keepCacheKeys)
    {
        var normalizedCacheType = NormalizeCacheType(cacheType);
        var dir = GetCacheTypeDirectory(CacheRootDirectory, normalizedCacheType);
        if (!Directory.Exists(dir))
        {
            return;
        }

        var keepFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cacheKey in keepCacheKeys)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                continue;
            }

            var hash = ComputeHash(cacheKey);
            keepFileNames.Add(hash + ".bin");
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(file);
                    continue;
                }

                if (name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && !keepFileNames.Contains(name))
                {
                    TryDeleteFile(file);
                }
            }
        }
        catch
        {
        }
    }

    private static string GetPayloadPath(string cacheRootDirectory, string cacheType, string cacheKey)
    {
        var hash = ComputeHash(cacheKey);
        return Path.Combine(GetCacheTypeDirectory(cacheRootDirectory, cacheType), hash + ".bin");
    }

    private static string ComputeHash(string cacheKey)
    {
        return MD5Helper.ComputeMD5(cacheKey);
    }

    private bool TryReadPayload(
        string cacheRootDirectory,
        string cacheType,
        string cacheKey,
        TimeSpan ttl,
        out byte[] payload,
        out TimeSpan remaining)
    {
        payload = [];
        remaining = default;
        var path = GetPayloadPath(cacheRootDirectory, cacheType, cacheKey);

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(path);
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var age = nowUtc - lastWriteUtc;
            if (age >= ttl)
            {
                TryDeleteFile(path);
                return false;
            }

            remaining = ttl - age;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length <= 0 || fs.Length > int.MaxValue)
            {
                TryDeleteFile(path);
                return false;
            }

            payload = ReadAllBytesByCopyTo(fs);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取缓存文件失败: {CacheKey}", cacheKey);
            return false;
        }
    }

    private void TryWritePayload(string cacheRootDirectory, string cacheType, string cacheKey, byte[] payload)
    {
        TryWritePayload(cacheRootDirectory, cacheType, cacheKey, payload, _timeProvider.GetUtcNow().UtcDateTime);
    }

    private void TryWritePayload(
        string cacheRootDirectory,
        string cacheType,
        string cacheKey,
        byte[] payload,
        DateTime nowUtc)
    {
        var path = GetPayloadPath(cacheRootDirectory, cacheType, cacheKey);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmpPath = path + ".tmp";

        try
        {
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                WriteAllBytesByCopyTo(fs, payload);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmpPath, path, overwrite: true);
            File.SetLastWriteTimeUtc(path, nowUtc);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "写入缓存文件失败: {CacheKey}", cacheKey);
            TryDeleteFile(tmpPath);
        }
    }

    private void TryDeletePayload(string cacheRootDirectory, string cacheType, string cacheKey)
    {
        TryDeleteFile(GetPayloadPath(cacheRootDirectory, cacheType, cacheKey));
    }

    private static unsafe byte[] ReadAllBytesByCopyTo(FileStream fs)
    {
        if (fs.Length <= 0 || fs.Length > int.MaxValue)
        {
            return [];
        }

        var bytes = new byte[(int)fs.Length];
        fixed (byte* p = bytes)
        {
            using var ms = new UnmanagedMemoryStream(p, fs.Length, fs.Length, FileAccess.Write);
            fs.Position = 0;
            fs.CopyTo(ms);
        }

        return bytes;
    }

    private static unsafe void WriteAllBytesByCopyTo(FileStream fs, byte[] bytes)
    {
        if (bytes.Length <= 0)
        {
            return;
        }

        fixed (byte* p = bytes)
        {
            using var ms = new UnmanagedMemoryStream(p, bytes.Length, bytes.Length, FileAccess.Read);
            ms.CopyTo(fs);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
