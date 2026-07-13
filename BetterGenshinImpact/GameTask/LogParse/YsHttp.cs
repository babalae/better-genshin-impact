using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.LogParse
{
    public class ApiResponse<T>
    {
        [JsonPropertyName("retcode")] public int Retcode { get; set; }

        [JsonPropertyName("message")] public string Message { get; set; }
        public Data<T> Data { get; set; }
    }

    public class Data<T>
    {
        [JsonPropertyName("uid")] public long Uid { get; set; }

        [JsonPropertyName("region")] public string Region { get; set; }

        [JsonPropertyName("account_id")] public long AccountId { get; set; }

        [JsonPropertyName("nickname")] public string Nickname { get; set; }

        [JsonPropertyName("date")] public string Date { get; set; }

        [JsonPropertyName("month")] public int Month { get; set; }

        [JsonPropertyName("optional_month")] public List<int> OptionalMonth { get; set; }

        [JsonPropertyName("data_month")] public int DataMonth { get; set; }

        [JsonPropertyName("page")] public int Page { get; set; }
        public List<T> List { get; set; }
    }

    public class ActionItem
    {
        [JsonPropertyName("action_id")] public int ActionId { get; set; }

        [JsonPropertyName("action")] public string Action { get; set; }

        [JsonPropertyName("time")] public string Time { get; set; }

        [JsonPropertyName("num")] public int Num { get; set; }
    }

    public class GameInfo
    {
        [JsonPropertyName("game_biz")] public string GameBiz { get; set; }

        [JsonPropertyName("region")] public string Region { get; set; }

        [JsonPropertyName("game_uid")] public string GameUid { get; set; }

        [JsonPropertyName("nickname")] public string Nickname { get; set; }

        [JsonPropertyName("level")] public int Level { get; set; }

        [JsonPropertyName("is_chosen")] public bool IsChosen { get; set; }

        [JsonPropertyName("region_name")] public string RegionName { get; set; }

        [JsonPropertyName("is_official")] public bool IsOfficial { get; set; }
    }

    public class YsClient
    {
        protected const string Accept = "Accept";
        protected const string Cookie = "Cookie";
        protected const string UserAgent = "User-Agent";
        protected const string X_Request_With = "X-Requested-With";
        protected const string DS = "DS";
        protected const string Referer = "Referer";
        protected const string Application_Json = "application/json";
        protected const string com_mihoyo_hyperion = "com.mihoyo.hyperion";
        protected const string com_mihoyo_hoyolab = "com.mihoyo.hoyolab";
        protected const string x_rpc_app_version = "x-rpc-app_version";
        protected const string x_rpc_device_id = "x-rpc-device_id";
        protected const string x_rpc_device_fp = "x-rpc-device_fp";
        protected const string x_rpc_client_type = "x-rpc-client_type";
        protected const string x_rpc_language = "X-Rpc-Language";

        public string UAContent =>
            $"Mozilla/5.0 (Linux; Android 13; Pixel 5 Build/TQ3A.230901.001; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBS/{AppVersion}";

        public string AppVersion => "2.71.1";

        protected string ApiSalt => "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";

        protected string ApiSalt2 => "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";

        protected string CreateSecret2(string url)
        {
            int t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string r = Random.Shared.Next(100000, 200000).ToString();
            string b = "";
            string q = "";
            string[] urls = url.Split('?');
            if (urls.Length == 2)
            {
                string[] queryParams = urls[1].Split('&').OrderBy(x => x).ToArray();
                q = string.Join("&", queryParams);
            }

            var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"salt={ApiSalt2}&t={t}&r={r}&b={b}&q={q}"));
            var check = Convert.ToHexString(bytes).ToLower();
            string result = $"{t},{r},{check}";
            return result;
        }

        protected readonly HttpClient _httpClient = new HttpClient();

        protected virtual async Task<ApiResponse<T>> CommonSendAsync<T>(HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            request.Version = HttpVersion.Version20;
            request.Headers.Add(Accept, Application_Json);
            request.Headers.Add(UserAgent, UAContent);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<T>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (apiResponse.Message == "未登录")
            {
                throw new NoLoginException();
            }

            return apiResponse;
        }

        /// <summary>
        /// 获取原神账号信息
        /// </summary>
        /// <param name="cookie"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ApiResponse<GameInfo>> GetGenshinGameRolesAsync(string cookie,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(cookie))
            {
                throw new ArgumentNullException(nameof(cookie));
            }

            var url = "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(Cookie, cookie);
            request.Headers.Add(DS, CreateSecret2(url));
            request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
            request.Headers.Add(x_rpc_app_version, AppVersion);
            request.Headers.Add(x_rpc_client_type, "5");
            request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
            var data = await CommonSendAsync<GameInfo>(request, cancellationToken);
            //data.List?.ForEach(x => x.Cookie = cookie);
            return data;
        }

        /// <summary>
        /// 旅行札记总览
        /// </summary>
        /// <param name="role"></param>
        /// <param name="month">0 当前月</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ApiResponse<ActionItem>> GetTravelsDiarySummaryAsync(GameInfo role, string cookie,
            int month = 0, CancellationToken cancellationToken = default)
        {
            var url =
                $"https://hk4e-api.mihoyo.com/event/ys_ledger/monthInfo?month={month}&bind_uid={role.GameUid}&bind_region={role.Region}&bbs_presentation_style=fullscreen&bbs_auth_required=true&utm_source=bbs&utm_medium=mys&utm_campaign=icon";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(Cookie, cookie);
            request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
            request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
            return await CommonSendAsync<ActionItem>(request, cancellationToken);
        }

        /// <summary>
        /// 旅行札记收入详情
        /// </summary>
        /// <param name="role"></param>
        /// <param name="month"></param>
        /// <param name="type">1原石，2摩拉</param>
        /// <param name="page">从1开始</param>
        /// <param name="limit">最大100</param>
        /// <param name="cancellationToken"></param>
        /// <returns>返回一页收入记录</returns>
        public async Task<ApiResponse<ActionItem>> GetTravelsDiaryDetailByPageAsync(GameInfo role, string cookie,
            int month, int type, int page, int limit = 100, CancellationToken cancellationToken = default)
        {
            var url =
                $"https://hk4e-api.mihoyo.com/event/ys_ledger/monthDetail?page={page}&month={month}&limit={limit}&type={type}&bind_uid={role.GameUid}&bind_region={role.Region}&bbs_presentation_style=fullscreen&bbs_auth_required=true&utm_source=bbs&utm_medium=mys&utm_campaign=icon";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(Cookie, cookie);
            request.Headers.Add(Referer, "https://webstatic.mihoyo.com/");
            request.Headers.Add(X_Request_With, com_mihoyo_hyperion);
            var data = await CommonSendAsync<ActionItem>(request, cancellationToken);
            //foreach (var item in data.List)
            //{
            //    item.Type = type;
            //}
            return data;
        }


        /// <summary>
        /// 旅行札记收入详情
        /// </summary>
        /// <param name="role"></param>
        /// <param name="month"></param>
        /// <param name="type">1原石，2摩拉</param>
        /// <param name="limit">最大100</param>
        /// <param name="cancellationToken"></param>
        /// <returns>返回该月所有收入记录</returns>
        public async Task<ApiResponse<ActionItem>> GetTravelsDiaryDetailAsync(GameInfo role, string cookie, int month,
            int type, int limit = 100, CancellationToken cancellationToken = default, ActionItem lastActionItem = null)
        {
            var data = await GetTravelsDiaryDetailByPageAsync(role, cookie, month, type, 1, limit, cancellationToken);
            if (lastActionItem != null)
            {
                if (DateTime.Parse(data.Data.List.FindLast(item => true).Time) <= DateTime.Parse(lastActionItem.Time))
                {
                    data.Data.List = data.Data.List
                        .Where(item => DateTime.Parse(item.Time) > DateTime.Parse(lastActionItem.Time)).ToList();
                    return data;
                }
            }

            if (data.Data.List.Count < limit)
            {
                return data;
            }

            for (int i = 2;; i++)
            {
                var addData =
                    await GetTravelsDiaryDetailByPageAsync(role, cookie, month, type, i, limit, cancellationToken);

                data.Data.List.AddRange(addData.Data.List);
                if (lastActionItem != null)
                {
                    if (DateTime.Parse(data.Data.List.FindLast(item => true).Time) <=
                        DateTime.Parse(lastActionItem.Time))
                    {
                        data.Data.List = data.Data.List
                            .Where(item => DateTime.Parse(item.Time) > DateTime.Parse(lastActionItem.Time)).ToList();
                        return data;
                    }
                }

                if (addData.Data.List.Count < limit)
                {
                    break;
                }
            }

            return data;
        }
    }
}