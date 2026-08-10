using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace BetterGenshinImpact.GameTask.Music.Service;

public sealed class MusicCoverService : IMusicCoverService
{
    private const string CacheType = "Artwork";
    private static readonly string CacheRootDirectory = Global.Absolute(Path.Combine("User", "Music"));
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(180);

    private readonly MemoryFileCache _fileCache;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MusicCoverService> _logger = App.GetLogger<MusicCoverService>();
    private int _stopRemoteRequests;

    public MusicCoverService(MemoryFileCache fileCache)
    {
        _fileCache = fileCache;
        _httpClient = HttpClientFactory.GetClient(
            "iTunesSearch",
            () => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    }

    public async Task<ImageSource?> GetCoverAsync(string songName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(songName))
        {
            return null;
        }

        var normalizedSongName = NormalizeSongName(songName);
        var bytes = await _fileCache.GetOrAddInDirectoryAsync<byte[]>(
            CacheRootDirectory,
            CacheType,
            $"v1:{normalizedSongName}",
            CacheTtl,
            token => SearchAndDownloadArtworkAsync(songName.Trim(), token),
            static value => value,
            static payload => payload,
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
