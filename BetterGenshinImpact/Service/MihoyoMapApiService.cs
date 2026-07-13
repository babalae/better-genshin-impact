using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using BetterGenshinImpact.Service.Model.MihoyoMap.Responses;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service
{
    public class MihoyoMapApiService : IMihoyoMapApiService
    {
        private readonly HttpClient _httpClient;
        private const string TreeEndpoint = "https://waf-api-takumi.mihoyo.com/common/map_user/ys_obc/v2/map/label/tree";
        private const string ListEndpoint = "https://waf-api-takumi.mihoyo.com/common/map_user/ys_obc/v3/map/point/list";
        private const string InfoEndpoint = "https://waf-api-takumi.mihoyo.com/common/map_user/ys_obc/v1/map/point/info";

        public MihoyoMapApiService()
        {
            _httpClient = HttpClientFactory.GetCommonSendClient();
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            request.Headers.Referrer = new Uri("https://act.mihoyo.com/");
            return request;
        }

        public async Task<ApiResponse<LabelTreeData>> GetLabelTreeAsync(LabelTreeRequest request, CancellationToken ct = default)
        {
            var url = $"{TreeEndpoint}?map_id={request.MapId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={Uri.EscapeDataString(request.Lang)}";
            using var httpRequest = CreateRequest(HttpMethod.Get, url);
            using var resp = await _httpClient.SendAsync(httpRequest, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<ApiResponse<LabelTreeData>>(json)!;
        }

        public async Task<ApiResponse<PointInfoData>> GetPointInfoAsync(PointInfoRequest request, CancellationToken ct = default)
        {
            var url = $"{InfoEndpoint}?map_id={request.MapId}&point_id={request.PointId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={Uri.EscapeDataString(request.Lang)}";
            using var httpRequest = CreateRequest(HttpMethod.Get, url);
            httpRequest.Headers.Add("x-rpc-map_version", "4.5");
            using var resp = await _httpClient.SendAsync(httpRequest, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<ApiResponse<PointInfoData>>(json)!;
        }

        public async Task<ApiResponse<PointListData>> GetPointListAsync(PointListRequest request, CancellationToken ct = default)
        {
            var labelIds = request.LabelIds != null && request.LabelIds.Count > 0
                ? string.Join(",", request.LabelIds.Select(x => x.ToString()))
                : string.Empty;
            var url = $"{ListEndpoint}?map_id={request.MapId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={Uri.EscapeDataString(request.Lang)}&label_ids={Uri.EscapeDataString(labelIds)}";
            using var httpRequest = CreateRequest(HttpMethod.Get, url);
            using var resp = await _httpClient.SendAsync(httpRequest, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<ApiResponse<PointListData>>(json)!;
        }
    }
}
