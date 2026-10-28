using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using BetterGenshinImpact.Infrastructure.Cache;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 远程图片服务：经 <see cref="IFileCache"/> 下载和落盘，用 <see cref="ImageSourceDecoder"/> 解码（支持 WebP、结果已冻结），
/// 解码结果按最近使用保留最多 <see cref="MaxDecodedImages"/> 张
/// </summary>
public sealed class RemoteImageService : IRemoteImageService
{
    /// <summary>
    /// 单张图片的下载上限，与文档图片原有限制一致
    /// </summary>
    internal const int MaxImageBytes = 20 * 1024 * 1024;

    /// <summary>
    /// 内存中保留的解码图片数量上限
    /// </summary>
    internal const int MaxDecodedImages = 512;

    /// <summary>
    /// 文件缓存
    /// </summary>
    private readonly IFileCache _cache;

    /// <summary>
    /// 命名 HttpClient 工厂，使用图片客户端
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<RemoteImageService> _logger;

    /// <summary>
    /// 保护 <see cref="_recent"/> 和 <see cref="_index"/>
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 解码图片，最近使用的在链表头部
    /// </summary>
    private readonly LinkedList<(string Key, ImageSource Image)> _recent = new();

    /// <summary>
    /// 键到链表节点的索引
    /// </summary>
    private readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> _index = new(StringComparer.Ordinal);

    /// <summary>
    /// 进行中的下载与解码，同一地址只执行一次
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _inflight = new(StringComparer.Ordinal);

    public RemoteImageService(IFileCache cache, IHttpClientFactory httpClientFactory, ILogger<RemoteImageService> logger)
    {
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryGetCached(CachePartition partition, string url, out ImageSource? image)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(GetKey(partition, url), out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    /// <inheritdoc />
    public Task<ImageSource?> GetAsync(CachePartition partition, string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        if (TryGetCached(partition, url, out var cached))
        {
            return Task.FromResult(cached);
        }

        // 在线程池启动，保证加载结束时移除记录发生在加入之后，失败的图片下次还能重试
        var key = GetKey(partition, url);
        var load = _inflight.GetOrAdd(key, _ => new Lazy<Task<ImageSource?>>(
            () => Task.Run(() => LoadAsync(partition, url, key))));
        return load.Value.WaitAsync(ct);
    }

    /// <inheritdoc />
    public Task<byte[]?> GetBytesAsync(CachePartition partition, string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"图片地址只支持 http 和 https：{url}", nameof(url));
        }

        return _cache.GetOrAddBytesAsync(partition, uri.AbsoluteUri, token => DownloadAsync(uri, token), ct);
    }

    /// <summary>
    /// 下载并解码；失败时记录日志并返回 null，不放入内存缓存，下次请求重新尝试
    /// </summary>
    private async Task<ImageSource?> LoadAsync(CachePartition partition, string url, string key)
    {
        try
        {
            var bytes = await GetBytesAsync(partition, url).ConfigureAwait(false);
            if (bytes is not { Length: > 0 })
            {
                return null;
            }

            var image = await ImageSourceDecoder.DecodeAsync(bytes).ConfigureAwait(false);
            Remember(key, image);
            return image;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "加载远程图片失败：{Url}", url);
            return null;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 用图片客户端下载，超过大小上限时中止
    /// </summary>
    private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.Images);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxImageBytes)
        {
            throw new InvalidDataException($"图片大小超过 {MaxImageBytes / 1024 / 1024} MB 限制。");
        }

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaxImageBytes)
            {
                throw new InvalidDataException($"图片大小超过 {MaxImageBytes / 1024 / 1024} MB 限制。");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>
    /// 放入解码缓存，超过上限时移除最久未使用的图片
    /// </summary>
    private void Remember(string key, ImageSource image)
    {
        lock (_lock)
        {
            if (_index.Remove(key, out var existing))
            {
                _recent.Remove(existing);
            }

            _index[key] = _recent.AddFirst((key, image));
            while (_recent.Count > MaxDecodedImages)
            {
                var last = _recent.Last!;
                _recent.RemoveLast();
                _index.Remove(last.Value.Key);
            }
        }
    }

    /// <summary>
    /// 解码缓存与进行中请求的键
    /// </summary>
    private static string GetKey(CachePartition partition, string url) => partition.Id + "\n" + url;
}
