using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.Oauth;
using BetterGenshinImpact.Service.Tavern.Model;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Tavern;

public class KongyingTavernApiService : IKongyingTavernApiService
{
    private const string DefaultBaseUrl = "https://cloud.yuanshen.site";
    private const string DefaultBasicAuthorization = "Y2xpZW50OnNlY3JldA==";

    private const string OauthTokenPath = "oauth/token";
    private const string ItemDocListPageBinMd5Path = "api/item_doc/list_page_bin_md5";
    private const string ItemDocListPageBinPathPrefix = "api/item_doc/list_page_bin/";
    private const string MarkerDocListPageBinMd5Path = "api/marker_doc/list_page_bin_md5";
    private const string MarkerDocListPageBinPathPrefix = "api/marker_doc/list_page_bin/";
    private const string IconDocAllBinMd5Path = "api/icon_doc/all_bin_md5";
    private const string IconDocAllBinPathPrefix = "api/icon_doc/all_bin/";

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private OauthTokenResponse? _cachedToken;
    private DateTimeOffset _cachedTokenExpiresAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(1);

    public KongyingTavernApiService()
    {
        _httpClient = HttpClientFactory.GetClient(
            "KongyingTavern",
            () => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
    }

    private static Uri BuildApiUri(string baseUrl, string apiPath, string? query = null)
    {
        var normalizedBaseUrl = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
        var normalizedApiPath = apiPath.TrimStart('/');
        while (normalizedApiPath.Contains("//", StringComparison.Ordinal))
        {
            normalizedApiPath = normalizedApiPath.Replace("//", "/", StringComparison.Ordinal);
        }

        var endpoint = new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), normalizedApiPath);
        if (string.IsNullOrWhiteSpace(query))
        {
            return endpoint;
        }

        return new UriBuilder(endpoint) { Query = query }.Uri;
    }

    private static Uri BuildUri(string apiPath, string? query = null)
    {
        return BuildApiUri(DefaultBaseUrl, apiPath, query);
    }

    private static Uri BuildUriWithId(string apiPathPrefix, string id)
    {
        var apiPath = apiPathPrefix + Uri.EscapeDataString(id);
        return BuildUri(apiPath);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("accept-language", "zh-CN,zh;q=0.9");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
        return request;
    }

    public async Task<OauthTokenResponse> GetTokenAsync(CancellationToken ct = default)
    {
        var uri = BuildUri(OauthTokenPath, query: "refresh_token=all&grant_type=client_credentials");
        using var request = CreateRequest(HttpMethod.Post, uri);

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", DefaultBasicAuthorization);
        using var resp = await _httpClient.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var result = JsonConvert.DeserializeObject<OauthTokenResponse>(json);
        return result ?? throw new InvalidOperationException("oauth/token 返回内容无法反序列化");
    }

    public async Task RefreshToken(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        SetCachedToken(token);
    }

    public async Task<IReadOnlyList<ItemTypeVo>> GetItemTypeListAsync(CancellationToken ct = default)
    {
        await EnsureAccessTokenAsync(ct);
        var pages = await GetItemDocPageMd5ListAsync(ct);
        var latestPage = pages.Count > 0
            ? pages.OrderByDescending(x => x.Time).First()
            : null;

        if (latestPage is null)
        {
            return [];
        }

        ct.ThrowIfCancellationRequested();
        var pageBytes = await GetItemDocPageBinAsync(latestPage.Md5, ct);
        var jsonBytes = TryDecompressGzip(pageBytes, out var decompressed) ? decompressed : pageBytes;
        var json = Encoding.UTF8.GetString(jsonBytes);
        var pageList = JsonConvert.DeserializeObject<List<ItemTypeVo>>(json);
        if (pageList is null)
        {
            throw new InvalidOperationException($"item_doc/list_page_bin/{latestPage.Md5} 返回内容无法反序列化为 List<ItemTypeVo>");
        }

        return pageList;
    }

    public async Task<IReadOnlyList<MarkerVo>> GetMarkerListAsync(CancellationToken ct = default)
    {
        await EnsureAccessTokenAsync(ct);
        var pages = await GetMarkerDocPageMd5ListAsync(ct);
        var latestPage = pages.Count > 0
            ? pages.OrderByDescending(x => x.Time).First()
            : null;

        if (latestPage is null)
        {
            return [];
        }

        ct.ThrowIfCancellationRequested();
        var pageBytes = await GetMarkerDocPageBinAsync(latestPage.Md5, ct);
        var jsonBytes = TryDecompressGzip(pageBytes, out var decompressed) ? decompressed : pageBytes;
        var json = Encoding.UTF8.GetString(jsonBytes);
        var pageList = JsonConvert.DeserializeObject<List<MarkerVo>>(json);
        if (pageList is null)
        {
            throw new InvalidOperationException($"marker_doc/list_page_bin/{latestPage.Md5} 返回内容无法反序列化为 List<MarkerVo>");
        }

        return pageList;
    }

    public async Task<IReadOnlyList<IconVo>> GetIconListAsync(CancellationToken ct = default)
    {
        await EnsureAccessTokenAsync(ct);
        var md5 = await GetIconDocAllBinMd5Async(ct);
        if (string.IsNullOrWhiteSpace(md5))
        {
            return [];
        }

        ct.ThrowIfCancellationRequested();
        var pageBytes = await GetIconDocAllBinAsync(md5, ct);
        var jsonBytes = TryDecompressGzip(pageBytes, out var decompressed) ? decompressed : pageBytes;
        var json = Encoding.UTF8.GetString(jsonBytes);
        var list = JsonConvert.DeserializeObject<List<IconVo>>(json);
        if (list is null)
        {
            throw new InvalidOperationException($"icon_doc/all_bin/{md5} 返回内容无法反序列化为 List<IconVo>");
        }

        return list;
    }

    private async Task<List<ListPageBinMd5Item>> GetItemDocPageMd5ListAsync(CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        var uri = BuildUri(ItemDocListPageBinMd5Path);
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var resp = await _httpClient.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        var result = JsonConvert.DeserializeObject<KongyingTavernResponse<List<ListPageBinMd5Item>>>(json);
        if (result is null)
        {
            throw new InvalidOperationException("item_doc/list_page_bin_md5 返回内容无法反序列化");
        }

        if (result.Error)
        {
            throw new InvalidOperationException($"item_doc/list_page_bin_md5 返回错误: {result.Message ?? "未知错误"}");
        }

        return result.Data ?? [];
    }

    private async Task<byte[]> GetItemDocPageBinAsync(string md5, CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        var uri = BuildUriWithId(ItemDocListPageBinPathPrefix, md5);
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var resp = await _httpClient.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<List<ListPageBinMd5Item>> GetMarkerDocPageMd5ListAsync(CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        var uri = BuildUri(MarkerDocListPageBinMd5Path);
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var resp = await _httpClient.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        var result = JsonConvert.DeserializeObject<KongyingTavernResponse<List<ListPageBinMd5Item>>>(json);
        if (result is null)
        {
            throw new InvalidOperationException("marker_doc/list_page_bin_md5 返回内容无法反序列化");
        }

        if (result.Error)
        {
            throw new InvalidOperationException($"marker_doc/list_page_bin_md5 返回错误: {result.Message ?? "未知错误"}");
        }

        return result.Data ?? [];
    }

    private async Task<byte[]> GetMarkerDocPageBinAsync(string md5, CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        var uri = BuildUriWithId(MarkerDocListPageBinPathPrefix, md5);
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var resp = await _httpClient.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<string> GetIconDocAllBinMd5Async(CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        var uri = BuildUri(IconDocAllBinMd5Path);
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var resp = await _httpClient.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (TryExtractMd5FromApiJson(json, out var md5))
        {
            return md5;
        }

        throw new InvalidOperationException($"{uri.AbsolutePath} 返回内容无法解析为 md5");
    }

    private async Task<byte[]> GetIconDocAllBinAsync(string md5, CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        var uri = BuildUriWithId(IconDocAllBinPathPrefix, md5);
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var resp = await _httpClient.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    private static bool TryExtractMd5FromApiJson(string json, out string md5)
    {
        md5 = string.Empty;
        KongyingTavernResponse<Newtonsoft.Json.Linq.JToken>? envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<KongyingTavernResponse<Newtonsoft.Json.Linq.JToken>>(json);
        }
        catch
        {
            envelope = null;
        }

        if (envelope is not null)
        {
            if (envelope.Error)
            {
                throw new InvalidOperationException(envelope.Message ?? "未知错误");
            }

            return TryExtractMd5FromData(envelope.Data, out md5);
        }

        try
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(json);
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.String)
            {
                md5 = token.ToObject<string>() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(md5);
            }

            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Object
                && token["md5"]?.Type == Newtonsoft.Json.Linq.JTokenType.String)
            {
                md5 = token["md5"]!.ToObject<string>() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(md5);
            }

            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Array)
            {
                return TryExtractMd5FromData(token, out md5);
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryExtractMd5FromData(Newtonsoft.Json.Linq.JToken? data, out string md5)
    {
        md5 = string.Empty;
        if (data is null)
        {
            return false;
        }

        if (data.Type == Newtonsoft.Json.Linq.JTokenType.String)
        {
            md5 = data.ToObject<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(md5);
        }

        if (data.Type == Newtonsoft.Json.Linq.JTokenType.Object)
        {
            md5 = data["md5"]?.ToObject<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(md5);
        }

        if (data.Type == Newtonsoft.Json.Linq.JTokenType.Array)
        {
            var items = data
                .OfType<Newtonsoft.Json.Linq.JObject>()
                .Select(x => new
                {
                    Md5 = x["md5"]?.ToObject<string>() ?? string.Empty,
                    Time = x["time"]?.ToObject<long?>(),
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Md5))
                .ToList();

            var latest = items.Count > 0
                ? items.OrderByDescending(x => x.Time ?? 0).FirstOrDefault()
                : null;

            md5 = latest?.Md5 ?? string.Empty;
            return !string.IsNullOrWhiteSpace(md5);
        }

        return false;
    }

    private void SetCachedToken(OauthTokenResponse token)
    {
        _cachedToken = token;
        _cachedTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.ExpiresIn));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    private bool IsTokenStillValid()
    {
        return _cachedToken is not null
               && DateTimeOffset.UtcNow < _cachedTokenExpiresAt.Subtract(RefreshBeforeExpiry);
    }

    private async Task EnsureAccessTokenAsync(CancellationToken ct)
    {
        if (IsTokenStillValid())
        {
            return;
        }

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (IsTokenStillValid())
            {
                return;
            }

            await RefreshToken(ct);
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static bool TryDecompressGzip(byte[] input, out byte[] output)
    {
        try
        {
            using var inputStream = new MemoryStream(input);
            using var gzip = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            gzip.CopyTo(outputStream);
            output = outputStream.ToArray();
            return true;
        }
        catch
        {
            output = [];
            return false;
        }
    }
}
