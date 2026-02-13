using BetterGenshinImpact.Service;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

            var image = await StaRunner.Instance.InvokeAsync(() =>
            {
                if (LooksLikeWebp(bytes))
                {
                    return LoadWebpFromBytes(bytes);
                }

                return LoadBitmapImageFromBytes(bytes);
            });

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

    private static ImageSource LoadBitmapImageFromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static ImageSource LoadWebpFromBytes(byte[] bytes)
    {
        using var img = Image.Load<Rgba32>(bytes);
        var width = img.Width;
        var height = img.Height;
        var stride = width * 4;
        var buffer = new byte[stride * height];

        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var rowOffset = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var p = row[x];
                    var a = p.A;
                    var i = rowOffset + x * 4;
                    buffer[i + 0] = Premultiply(p.B, a);
                    buffer[i + 1] = Premultiply(p.G, a);
                    buffer[i + 2] = Premultiply(p.R, a);
                    buffer[i + 3] = a;
                }
            }
        });

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
        bmp.Freeze();
        return bmp;
    }

    private static byte Premultiply(byte c, byte a)
    {
        return (byte)((c * a + 127) / 255);
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

    private static bool LooksLikeWebp(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        try
        {
            return bytes[0] == (byte)'R'
                   && bytes[1] == (byte)'I'
                   && bytes[2] == (byte)'F'
                   && bytes[3] == (byte)'F'
                   && bytes[8] == (byte)'W'
                   && bytes[9] == (byte)'E'
                   && bytes[10] == (byte)'B'
                   && bytes[11] == (byte)'P';
        }
        catch
        {
            return false;
        }
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

file sealed class StaRunner
{
    public static StaRunner Instance { get; } = new();

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    private StaRunner()
    {
        _thread = new Thread(Run) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
