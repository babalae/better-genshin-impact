using BetterGenshinImpact.Infrastructure.Cache;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace BetterGenshinImpact.GameTask.Music.Service;

public sealed class MusicCoverService : IMusicCoverService
{
    /// <summary>
    /// 封面缓存（Data/Cache/Files/music-artwork，180 天）
    /// </summary>
    private readonly IFileCache _fileCache;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MusicCoverService> _logger = App.GetLogger<MusicCoverService>();
    private int _stopRemoteRequests;

    /// <param name="fileCache">文件缓存</param>
    /// <param name="httpClientFactory">命名 HttpClient 工厂，搜索和封面下载都使用图片客户端</param>
    public MusicCoverService(IFileCache fileCache, IHttpClientFactory httpClientFactory)
    {
        _fileCache = fileCache;
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.Images);
    }

    public async Task<ImageSource?> GetCoverAsync(string songName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(songName))
        {
            return null;
        }

        var normalizedSongName = NormalizeSongName(songName);
        var bytes = await _fileCache.GetOrAddBytesAsync(
            CachePartitions.MusicArtwork,
            $"v1:{normalizedSongName}",
            token => SearchAndDownloadArtworkAsync(songName.Trim(), token),
            cancellationToken);

        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return await ImageSourceDecoder.DecodeAsync(bytes);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "解码音乐封面失败：{SongName}", songName);
            return null;
        }
    }

    internal static string NormalizeSongName(string songName)
    {
        var normalized = songName.Normalize(NormalizationForm.FormKC).Trim();
        var parts = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToUpperInvariant();
    }

    private async Task<byte[]?> SearchAndDownloadArtworkAsync(
        string songName,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopRemoteRequests) != 0)
        {
            return null;
        }

        try
        {
            var requestUri = new Uri(
                $"https://itunes.apple.com/search?term={Uri.EscapeDataString(songName)}&entity=song&limit=1");
            using var searchResponse = await _httpClient
                .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            searchResponse.EnsureSuccessStatusCode();

            var json = await searchResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<ItunesSearchResponse>(json);
            var artworkUrl = result?.Results.FirstOrDefault()?.ArtworkUrl100;
            if (string.IsNullOrWhiteSpace(artworkUrl)
                || !Uri.TryCreate(artworkUrl, UriKind.Absolute, out var artworkUri))
            {
                return null;
            }

            using var artworkResponse = await _httpClient
                .GetAsync(artworkUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            artworkResponse.EnsureSuccessStatusCode();
            return await artworkResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Interlocked.Exchange(ref _stopRemoteRequests, 1);
            _logger.LogDebug(e, "通过 iTunes Search API 获取音乐封面失败，已停止后续远程请求：{SongName}", songName);
            return null;
        }
    }

    private sealed class ItunesSearchResponse
    {
        [JsonProperty("results")]
        public List<ItunesSearchItem> Results { get; init; } = [];
    }

    private sealed class ItunesSearchItem
    {
        [JsonProperty("artworkUrl100")]
        public string ArtworkUrl100 { get; init; } = string.Empty;
    }
}
