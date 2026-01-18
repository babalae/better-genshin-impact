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
using LazyCache;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service
{
    public class MihoyoMapApiService : IMihoyoMapApiService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppCache _cache;
        private const string TreeEndpoint = "https://waf-api-takumi.mihoyo.com/common/map_user/ys_obc/v2/map/label/tree";
        private const string ListEndpoint = "https://waf-api-takumi.mihoyo.com/common/map_user/ys_obc/v3/map/point/list";

        public MihoyoMapApiService(IAppCache cache)
        {
            _cache = cache;
            _httpClient = HttpClientFactory.GetCommonSendClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://act.mihoyo.com/");
        }

        public async Task<ApiResponse<LabelTreeData>> GetLabelTreeAsync(LabelTreeRequest request, CancellationToken ct = default)
        {
            var url = $"{TreeEndpoint}?map_id={request.MapId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={Uri.EscapeDataString(request.Lang)}";
            var resp = await _httpClient.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<ApiResponse<LabelTreeData>>(json)!;
        }

        public async Task<ApiResponse<PointListData>> GetPointListAsync(PointListRequest request, CancellationToken ct = default)
        {
            var labelIds = request.LabelIds != null && request.LabelIds.Count > 0
                ? string.Join(",", request.LabelIds.Select(x => x.ToString()))
                : string.Empty;
            var url = $"{ListEndpoint}?map_id={request.MapId}&app_sn={Uri.EscapeDataString(request.AppSn)}&lang={Uri.EscapeDataString(request.Lang)}&label_ids={Uri.EscapeDataString(labelIds)}";
            var resp = await _httpClient.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<ApiResponse<PointListData>>(json)!;
        }

        public Task<ApiResponse<PointListData>> GetPointListCacheAsync(PointListRequest request, CancellationToken ct = default)
        {
            var labelIds = request.LabelIds?.Distinct().OrderBy(x => x).ToArray() ?? Array.Empty<int>();
            var key = $"mihoyo-map:point-list:{request.MapId}:{request.AppSn}:{request.Lang}:{string.Join(",", labelIds)}";
            var cachedRequest = new PointListRequest
            {
                MapId = request.MapId,
                AppSn = request.AppSn,
                Lang = request.Lang,
                LabelIds = labelIds.ToList()
            };

            return _cache.GetOrAddAsync(
                    key,
                    async (ICacheEntry entry) =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2);
                        return await GetPointListAsync(cachedRequest, CancellationToken.None);
                    })
                .WaitAsync(ct);
        }
    }
}
