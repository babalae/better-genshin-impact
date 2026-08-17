using BetterGenshinImpact.Service;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace BetterGenshinImpact.ViewModel;

internal static class MapIconImageCache
{
    private const string CacheType = "map-icon-image";
    private static readonly HttpClient _http = new();
    private static readonly TimeSpan _ttl = TimeSpan.FromDays(20);
    private static readonly ConcurrentDictionary<string, CacheEntry> _decodedCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new(StringComparer.Ordinal);
    private static readonly TimeProvider _timeProvider;
    private static readonly MemoryFileCache _fileCache;

    static MapIconImageCache()
    {
        _timeProvider = App.GetService<TimeProvider>() ?? TimeProvider.System;
        _fileCache = App.GetService<MemoryFileCache>() ?? CreateDefaultMemoryFileCache();
    }

    public static event EventHandler<string>? ImageUpdated;

    public static ImageSource? TryGet(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!_decodedCache.TryGetValue(url, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _decodedCache.TryRemove(url, out _);
            return null;
        }

        return entry.Image;
    }

    public static Task<ImageSource?> GetAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        var cached = TryGet(url);
        if (cached != null)
        {
            return Task.FromResult<ImageSource?>(cached);
        }

        var task = _inflight.GetOrAdd(url, u => LoadAndDecodeAsync(u, CancellationToken.None));
        return task.WaitAsync(ct);
    }

    private static async Task<ImageSource?> LoadAndDecodeAsync(string url, CancellationToken ct)
    {
        try
        {
            var bytes = await _fileCache.GetOrAddAsync<byte[]>(
                CacheType,
                url,
                _ttl,
                token => LoadBytesAsync(url, token),
                obj => obj,
                payload => payload,
                ct);

            if (bytes is not { Length: > 0 })
            {
                return null;
            }

            var image = await ImageSourceDecoder.DecodeAsync(bytes);

            if (image == null)
            {
                return null;
            }

            var entry = new CacheEntry(image, _timeProvider.GetUtcNow().Add(_ttl));
            _decodedCache[url] = entry;
            ImageUpdated?.Invoke(null, url);
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.TryRemove(url, out _);
        }
    }

    private static async Task<byte[]?> LoadBytesAsync(string url, CancellationToken ct)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return await _http.GetByteArrayAsync(url, ct);
        }

        var uri = ToAbsoluteOrRelativeUri(url);
        return await StaRunner.Instance.InvokeAsync(() => TryReadBytesFromUri(uri));
    }

    private static MemoryFileCache CreateDefaultMemoryFileCache()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = new MemoryCacheProvider(memoryCache);
        var appCache = new CachingService(new Lazy<ICacheProvider>(() => provider));
        return new MemoryFileCache(appCache, TimeProvider.System, NullLogger<MemoryFileCache>.Instance);
    }

    private static byte[]? TryReadBytesFromUri(Uri uri)
    {
        try
        {
            if (uri.IsFile && File.Exists(uri.LocalPath))
            {
                return File.ReadAllBytes(uri.LocalPath);
            }

            if (Application.GetResourceStream(uri) is { } res)
            {
                using var s = res.Stream;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }

            if (Application.GetContentStream(uri) is { } content)
            {
                using var s = content.Stream;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch
        {
        }

        return null;
    }

    private static Uri ToAbsoluteOrRelativeUri(string iconUrl)
    {
        if (iconUrl.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(iconUrl, UriKind.Absolute);
        }

        if (Uri.TryCreate(iconUrl, UriKind.Absolute, out var abs))
        {
            return abs;
        }

        var basePath = AppContext.BaseDirectory;
        var fullPath = Path.Combine(basePath, iconUrl);
        return new Uri(fullPath, UriKind.Absolute);
    }

    private readonly record struct CacheEntry(ImageSource Image, DateTimeOffset ExpiresAtUtc);
}
