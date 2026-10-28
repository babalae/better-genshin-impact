using BetterGenshinImpact.Infrastructure.Cache;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 地图点位图标的静态门面。画布控件依赖 <see cref="ImageUpdated"/> 事件重绘，因此保留静态入口；
/// 网络图标交给 <see cref="IRemoteImageService"/>（文件缓存 + 有上限的解码缓存），程序内的本地图标直接解码
/// </summary>
internal static class MapIconImageCache
{
    /// <summary>
    /// 远程图片服务，每次从容器取得，避免类型初始化早于容器创建时永久为空
    /// </summary>
    private static IRemoteImageService? Remote => App.GetService<IRemoteImageService>();

    /// <summary>
    /// 程序内置的本地图标（pack 资源、程序目录下的文件），数量由程序资源决定
    /// </summary>
    private static readonly ConcurrentDictionary<string, ImageSource> _localImages = new(StringComparer.Ordinal);

    /// <summary>
    /// 进行中的加载，同一图标只加载一次、只通知一次
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new(StringComparer.Ordinal);

    /// <summary>
    /// 图标加载完成，参数为图标地址，在后台线程触发
    /// </summary>
    public static event EventHandler<string>? ImageUpdated;

    /// <summary>
    /// 已加载时同步返回图标，否则返回 null
    /// </summary>
    public static ImageSource? TryGet(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (IsRemote(url))
        {
            return Remote is not null && Remote.TryGetCached(CachePartitions.MapIcons, url, out var image) ? image : null;
        }

        return _localImages.TryGetValue(url, out var local) ? local : null;
    }

    /// <summary>
    /// 加载图标，失败返回 null；ct 只取消本次等待，不中断共享的加载
    /// </summary>
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

        // 在线程池启动，保证加载结束时移除记录发生在加入之后，失败的图标下次还能重试
        var task = _inflight.GetOrAdd(url, u => Task.Run(() => LoadAsync(u)));
        return task.WaitAsync(ct);
    }

    /// <summary>
    /// 加载并通知画布重绘
    /// </summary>
    private static async Task<ImageSource?> LoadAsync(string url)
    {
        try
        {
            ImageSource? image;
            if (IsRemote(url))
            {
                image = Remote is null ? null : await Remote.GetAsync(CachePartitions.MapIcons, url).ConfigureAwait(false);
            }
            else
            {
                var uri = ToAbsoluteOrRelativeUri(url);
                var bytes = await StaRunner.Instance.InvokeAsync(() => TryReadBytesFromUri(uri)).ConfigureAwait(false);
                image = bytes is { Length: > 0 } ? await ImageSourceDecoder.DecodeAsync(bytes).ConfigureAwait(false) : null;
                if (image != null)
                {
                    _localImages[url] = image;
                }
            }

            if (image != null)
            {
                ImageUpdated?.Invoke(null, url);
            }

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

    /// <summary>
    /// 是否为网络图标
    /// </summary>
    private static bool IsRemote(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

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
}
