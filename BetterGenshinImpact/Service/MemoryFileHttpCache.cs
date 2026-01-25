using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Http;
using LazyCache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service;

public sealed class MemoryFileHttpCache
{
    private static ReadOnlySpan<byte> Magic => "BGI1"u8;
    private const int HeaderSize = 4 + 8 + 4;

    private readonly IAppCache _memoryCache;
    private readonly HttpClient _httpClient;
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
        _httpClient = HttpClientFactory.GetCommonSendClient();
        _rootDirectory = Global.Absolute(Path.Combine("Cache", Global.Version, "Http"));
        Directory.CreateDirectory(_rootDirectory);
    }

    public Task<byte[]?> GetOrAddBytesFromHttpAsync(string cacheKey, Uri uri, TimeSpan ttl, CancellationToken ct = default)
    {
        return GetOrAddBytesAsync(
            cacheKey,
            ttl,
            httpFactory: token => _httpClient.GetByteArrayAsync(uri, token),
            ct);
    }

    public Task<string?> GetOrAddStringFromHttpAsync(string cacheKey, Uri uri, TimeSpan ttl, CancellationToken ct = default)
    {
        return GetOrAddStringAsync(
            cacheKey,
            ttl,
            httpFactory: token => _httpClient.GetStringAsync(uri, token),
            ct);
    }

    public Task<T?> GetOrAddJsonFromHttpAsync<T>(string cacheKey, Uri uri, TimeSpan ttl, JsonSerializerSettings? settings = null,
        CancellationToken ct = default)
    {
        return GetOrAddJsonAsync<T>(
            cacheKey,
            ttl,
            httpFactory: token => _httpClient.GetStringAsync(uri, token),
            settings,
            ct);
    }

    public Task<byte[]?> GetOrAddBytesAsync(string cacheKey, TimeSpan ttl, Func<CancellationToken, Task<byte[]>> httpFactory,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("cacheKey cannot be empty.", nameof(cacheKey));
        }

        return _memoryCache.GetOrAddAsync<byte[]?>(
                cacheKey,
                async entry =>
                {
                    if (TryReadPayload(cacheKey, ".bytes", out var payload, out var remaining))
                    {
                        entry.AbsoluteExpirationRelativeToNow = remaining;
                        return payload;
                    }

                    entry.AbsoluteExpirationRelativeToNow = ttl;
                    var bytes = await httpFactory(CancellationToken.None).ConfigureAwait(false);
                    if (bytes is { Length: > 0 })
                    {
                        TryWritePayload(cacheKey, ".bytes", bytes, _timeProvider.GetUtcNow().Add(ttl));
                    }

                    return bytes;
                })
            .WaitAsync(ct);
    }

    public Task<string?> GetOrAddStringAsync(string cacheKey, TimeSpan ttl, Func<CancellationToken, Task<string>> httpFactory,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("cacheKey cannot be empty.", nameof(cacheKey));
        }

        return _memoryCache.GetOrAddAsync<string?>(
                cacheKey,
                async entry =>
                {
                    if (TryReadPayload(cacheKey, ".str", out var payload, out var remaining))
                    {
                        entry.AbsoluteExpirationRelativeToNow = remaining;
                        try
                        {
                            return Encoding.UTF8.GetString(payload);
                        }
                        catch
                        {
                            TryDeletePayload(cacheKey, ".str");
                        }
                    }

                    entry.AbsoluteExpirationRelativeToNow = ttl;
                    var str = await httpFactory(CancellationToken.None).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(str))
                    {
                        TryWritePayload(cacheKey, ".str", Encoding.UTF8.GetBytes(str), _timeProvider.GetUtcNow().Add(ttl));
                    }

                    return str;
                })
            .WaitAsync(ct);
    }

    public Task<T?> GetOrAddJsonAsync<T>(string cacheKey, TimeSpan ttl, Func<CancellationToken, Task<string>> httpFactory,
        JsonSerializerSettings? settings = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("cacheKey cannot be empty.", nameof(cacheKey));
        }

        return _memoryCache.GetOrAddAsync<T?>(
                cacheKey,
                async entry =>
                {
                    if (TryReadPayload(cacheKey, ".json", out var payload, out var remaining))
                    {
                        entry.AbsoluteExpirationRelativeToNow = remaining;
                        try
                        {
                            var json = Encoding.UTF8.GetString(payload);
                            return JsonConvert.DeserializeObject<T>(json, settings);
                        }
                        catch
                        {
                            TryDeletePayload(cacheKey, ".json");
                        }
                    }

                    entry.AbsoluteExpirationRelativeToNow = ttl;
                    var jsonFromHttp = await httpFactory(CancellationToken.None).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(jsonFromHttp))
                    {
                        return default;
                    }

                    T? obj;
                    try
                    {
                        obj = JsonConvert.DeserializeObject<T>(jsonFromHttp, settings);
                    }
                    catch
                    {
                        return default;
                    }

                    TryWritePayload(cacheKey, ".json", Encoding.UTF8.GetBytes(jsonFromHttp), _timeProvider.GetUtcNow().Add(ttl));
                    return obj;
                })
            .WaitAsync(ct);
    }

    private string GetPayloadPath(string cacheKey, string suffix)
    {
        var hash = ComputeHash(cacheKey);
        var dir = Path.Combine(_rootDirectory, hash[..2], hash[2..4]);
        return Path.Combine(dir, hash + suffix + ".bin");
    }

    private static string ComputeHash(string cacheKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private bool TryReadPayload(string cacheKey, string suffix, out byte[] payload, out TimeSpan remaining)
    {
        payload = [];
        remaining = default;
        var path = GetPayloadPath(cacheKey, suffix);

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < HeaderSize)
            {
                TryDeleteFile(path);
                return false;
            }

            Span<byte> header = stackalloc byte[HeaderSize];
            fs.ReadExactly(header);

            if (!header[..4].SequenceEqual(Magic))
            {
                TryDeleteFile(path);
                return false;
            }

            var expiresUnixMs = BinaryPrimitives.ReadInt64LittleEndian(header[4..12]);
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header[12..16]);

            if (payloadLength < 0 || fs.Length != HeaderSize + payloadLength)
            {
                TryDeleteFile(path);
                return false;
            }

            var nowUnixMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            if (expiresUnixMs <= nowUnixMs)
            {
                TryDeleteFile(path);
                return false;
            }

            remaining = TimeSpan.FromMilliseconds(expiresUnixMs - nowUnixMs);
            payload = new byte[payloadLength];
            fs.ReadExactly(payload);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取缓存文件失败: {CacheKey}", cacheKey);
            return false;
        }
    }

    private void TryWritePayload(string cacheKey, string suffix, byte[] payload, DateTimeOffset expiresAtUtc)
    {
        var path = GetPayloadPath(cacheKey, suffix);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmpPath = path + ".tmp";

        try
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            Magic.CopyTo(header);
            BinaryPrimitives.WriteInt64LittleEndian(header[4..12], expiresAtUtc.ToUnixTimeMilliseconds());
            BinaryPrimitives.WriteInt32LittleEndian(header[12..16], payload.Length);

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(header);
                fs.Write(payload);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmpPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "写入缓存文件失败: {CacheKey}", cacheKey);
            TryDeleteFile(tmpPath);
        }
    }

    private void TryDeletePayload(string cacheKey, string suffix)
    {
        TryDeleteFile(GetPayloadPath(cacheKey, suffix));
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

