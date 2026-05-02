using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using BetterGenshinImpact.Service.Model.MihoyoMap.Responses;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service;

public class HoYoLabMapApiService : IHoYoLabMapApiService
{
    private readonly HttpClient _httpClient;
    private const string TreeEndpoint = "https://sg-public-api-static.hoyolab.com/common/map_user/ys_obc/v2/map/label/tree";
    private const string ListEndpoint = "https://sg-public-api-static.hoyolab.com/common/map_user/ys_obc/v3/map/point/list";
    private const string InfoEndpoint = "https://sg-public-api-static.hoyolab.com/common/map_user/ys_obc/v1/map/point/info";
    private const string DefaultLang = MapMaskConfig.HoYoLabLanguageEnUs;

    public HoYoLabMapApiService()
    {
        _httpClient = HttpClientFactory.GetCommonSendClient();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        request.Headers.Referrer = new Uri("https://act.hoyolab.com/");
        return request;
    }

    private static T DeserializeRequired<T>(string json)
    {
        var result = JsonConvert.DeserializeObject<T>(json);
        if (result == null)
        {
            throw new JsonException($"Failed to deserialize {typeof(T).Name}. The API returned an empty or invalid JSON.");
        }

        return result;
    }

    public static string NormalizeLanguage(string? lang)
    {
        var normalized = (lang ?? string.Empty).Trim().Replace('_', '-').Replace(' ', '-').ToLowerInvariant();
        return normalized switch
        {
            MapMaskConfig.HoYoLabLanguagePtPt => MapMaskConfig.HoYoLabLanguagePtPt,
            MapMaskConfig.HoYoLabLanguageEsEs => MapMaskConfig.HoYoLabLanguageEsEs,
            MapMaskConfig.HoYoLabLanguageEnUs => MapMaskConfig.HoYoLabLanguageEnUs,
            _ => DefaultLang
        };
    }

    private static string GetCurrentLanguage()
    {
        var lang = TaskContext.Instance().Config.MapMaskConfig.HoYoLabLanguage;
        return NormalizeLanguage(lang);
    }

    public async Task<ApiResponse<LabelTreeData>> GetLabelTreeAsync(LabelTreeRequest request, CancellationToken ct = default)
    {
        var lang = GetCurrentLanguage();
        var url = $"{TreeEndpoint}?map_id={request.MapId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={lang}";
        using var httpRequest = CreateRequest(HttpMethod.Get, url);
        using var resp = await _httpClient.SendAsync(httpRequest, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return DeserializeRequired<ApiResponse<LabelTreeData>>(json);
    }

    public async Task<ApiResponse<PointInfoData>> GetPointInfoAsync(PointInfoRequest request, CancellationToken ct = default)
    {
        var lang = GetCurrentLanguage();
        var url = $"{InfoEndpoint}?map_id={request.MapId}&point_id={request.PointId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={lang}";
        using var httpRequest = CreateRequest(HttpMethod.Get, url);
        httpRequest.Headers.Add("x-rpc-map_version", "4.5");
        using var resp = await _httpClient.SendAsync(httpRequest, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return DeserializeRequired<ApiResponse<PointInfoData>>(json);
    }

    public async Task<ApiResponse<PointListData>> GetPointListAsync(PointListRequest request, CancellationToken ct = default)
    {
        var lang = GetCurrentLanguage();
        var labelIds = request.LabelIds != null && request.LabelIds.Count > 0
            ? string.Join(",", request.LabelIds.Select(x => x.ToString()))
            : string.Empty;
        var url = $"{ListEndpoint}?map_id={request.MapId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={lang}&label_ids={Uri.EscapeDataString(labelIds)}";
        using var httpRequest = CreateRequest(HttpMethod.Get, url);
        using var resp = await _httpClient.SendAsync(httpRequest, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return DeserializeRequired<ApiResponse<PointListData>>(json);
    }
}