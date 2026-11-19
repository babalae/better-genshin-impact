using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Infrastructure.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.HoYoLab;

/// <summary>
/// HoYoLAB 互动地图的账号标记（网页上的“图钉”）接口，需要登录 Cookie。
/// 接口与请求头取自网页实际请求，不需要 DS 签名。
/// </summary>
public class HoYoLabMarkApiService
{
    /// <summary>
    /// 需要登录态的接口根地址（注意不是 -static 域名）
    /// </summary>
    private const string BaseUrl = "https://sg-public-api.hoyolab.com/common/map_user/ys_obc";

    /// <summary>
    /// 网页使用的应用标识
    /// </summary>
    private const string AppSn = "ys_obc";

    /// <summary>
    /// 表示登录失效的返回码
    /// </summary>
    private static readonly HashSet<int> SessionExpiredRetcodes = [-100, 10001];

    /// <summary>
    /// 发送请求的客户端
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 使用指定客户端创建服务（测试时传入替身处理器）
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    public HoYoLabMarkApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 依赖注入入口，使用游戏接口命名客户端
    /// </summary>
    /// <param name="httpClientFactory">命名 HttpClient 工厂</param>
    public HoYoLabMarkApiService(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory.CreateClient(HttpClientNames.GameApi))
    {
    }

    /// <summary>
    /// 读取账号已标记的点位 ID
    /// </summary>
    /// <param name="cookieHeader">登录 Cookie 请求头</param>
    /// <param name="mapId">地图 ID，提瓦特为 2</param>
    /// <param name="lang">接口语言，如 pt-pt</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已标记的点位 ID 集合</returns>
    public async Task<IReadOnlySet<long>> GetMarkedPointIdsAsync(string cookieHeader, int mapId, string lang, CancellationToken ct)
    {
        var url = $"{BaseUrl}/v1/map/point/mark_map_point_list?map_id={mapId}&app_sn={AppSn}&lang={Uri.EscapeDataString(lang)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var root = await SendAsync(request, cookieHeader, ct);

        var result = new HashSet<long>();
        if (root["data"]?["list"] is JArray list)
        {
            foreach (var item in list)
            {
                var pointId = item["point_id"]?.Value<long?>();
                if (pointId != null)
                {
                    result.Add(pointId.Value);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 在账号上标记点位
    /// </summary>
    public Task AddMarkAsync(string cookieHeader, int mapId, long pointId, string lang, CancellationToken ct)
    {
        return PostMarkAsync("add_mark_map_point", cookieHeader, mapId, pointId, lang, ct);
    }

    /// <summary>
    /// 在账号上取消标记点位
    /// </summary>
    public Task DeleteMarkAsync(string cookieHeader, int mapId, long pointId, string lang, CancellationToken ct)
    {
        return PostMarkAsync("new_del_mark_map_point", cookieHeader, mapId, pointId, lang, ct);
    }

    /// <summary>
    /// 标记与取消标记共用的 POST 请求，请求体与网页一致
    /// </summary>
    private async Task PostMarkAsync(string action, string cookieHeader, int mapId, long pointId, string lang, CancellationToken ct)
    {
        var body = JsonConvert.SerializeObject(new { map_id = mapId, point_id = pointId, app_sn = AppSn, lang });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/map/point/{action}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        await SendAsync(request, cookieHeader, ct);
    }

    /// <summary>
    /// 补齐网页请求头并发送，按返回码区分登录失效与其他错误
    /// </summary>
    private async Task<JObject> SendAsync(HttpRequestMessage request, string cookieHeader, CancellationToken ct)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        request.Headers.Referrer = new Uri("https://act.hoyolab.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://act.hoyolab.com");
        request.Headers.TryAddWithoutValidation("x-rpc-map_version", "4.5");
        request.Headers.TryAddWithoutValidation("x-rpc-lrsag", "");
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JObject.Parse(json);

        var retcode = root["retcode"]?.Value<int>() ?? -1;
        var message = root["message"]?.Value<string>() ?? string.Empty;
        if (retcode == 0)
        {
            return root;
        }

        if (SessionExpiredRetcodes.Contains(retcode))
        {
            throw new HoYoLabSessionExpiredException(message);
        }

        throw new HoYoLabApiException(retcode, message);
    }
}
