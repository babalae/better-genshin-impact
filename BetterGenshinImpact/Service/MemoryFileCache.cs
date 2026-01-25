using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Security;
using LazyCache;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

public sealed class MemoryFileHttpCache
{
    private readonly IAppCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemoryFileHttpCache> _logger;
    private readonly string _rootDirectory;

    public MemoryFileHttpCache(
        IAppCache memoryCache,
        TimeProvider timeProvider,
        ILogger<MemoryFileHttpCache> logger)
    {
        _memoryCache = memoryCache;
        _timeProvider = timeProvider;
        _logger = logger;
        _rootDirectory = Global.Absolute(Path.Combine("User", "Cache", "Http"));
        Directory.CreateDirectory(_rootDirectory);
    }

    public Task<T?> GetOrAddAsync<T>(
        string cacheKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> factory,
        Func<T, byte[]> serialize,
        Func<byte[], T?> deserialize,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("cacheKey cannot be empty.", nameof(cacheKey));
        }

        return _memoryCache.GetOrAddAsync<T?>(
                cacheKey,
                async entry =>
                {
                    if (TryReadPayload(cacheKey, ttl, out var payload, out var remaining))
                    {
                        entry.AbsoluteExpirationRelativeToNow = remaining;
                        try
                        {
                            return deserialize(payload);
                        }
                        catch
                        {
                            TryDeletePayload(cacheKey);
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
                        TryWritePayload(cacheKey, bytes);
                    }

                    return obj;
                })
            .WaitAsync(ct);
    }

    private string GetPayloadPath(string cacheKey)
    {
        var hash = ComputeHash(cacheKey);
        var dir = Path.Combine(_rootDirectory, hash[..2], hash[2..4]);
        return Path.Combine(dir, hash + ".bin");
    }

    private static string ComputeHash(string cacheKey)
    {
        return MD5Helper.ComputeMD5(cacheKey);
    }

    private bool TryReadPayload(string cacheKey, TimeSpan ttl, out byte[] payload, out TimeSpan remaining)
    {
        payload = [];
        remaining = default;
        var path = GetPayloadPath(cacheKey);

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

    private void TryWritePayload(string cacheKey, byte[] payload)
    {
        TryWritePayload(cacheKey, payload, _timeProvider.GetUtcNow().UtcDateTime);
    }

    private void TryWritePayload(string cacheKey, byte[] payload, DateTime nowUtc)
    {
        var path = GetPayloadPath(cacheKey);
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

    private void TryDeletePayload(string cacheKey)
    {
        TryDeleteFile(GetPayloadPath(cacheKey));
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
