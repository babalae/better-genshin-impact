using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.UseRedeemCode.Model;
using BetterGenshinImpact.Helpers.Http;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

/// <summary>
/// 获取直播的前瞻兑换码
/// </summary>
public class GetLiveRedeemCode
{
    private static readonly string BBS_URL = "https://bbs-api.mihoyo.com";
    private readonly HttpClient _httpClient = HttpClientFactory.GetCommonSendClient();

    private readonly Dictionary<string, string> _url = new()
    {
        { "act_id_1", $"{BBS_URL}/painter/api/user_instant/list?offset=0&size=20&uid=75276539" },
        { "act_id_2", $"{BBS_URL}/painter/api/user_instant/list?offset=0&size=20&uid=75276550" },
        { "index", "https://api-takumi.mihoyo.com/event/miyolive/index" },
        { "code", "https://api-takumi-static.mihoyo.com/event/miyolive/refreshCode" }
    };

    private async Task<JObject> GetDataAsync(string type, Dictionary<string, string>? data = null)
    {
        try
        {
            HttpResponseMessage res;
            var request = new HttpRequestMessage(HttpMethod.Get, _url[type]);

            // 为所有需要的请求添加 act_id header
            if ((type == "index" || type == "code") && data != null && data.TryGetValue("actId", out var actId))
            {
                request.Headers.Add("x-rpc-act_id", actId);
            }

            // 为code类型添加查询参数
            if (type == "code" && data != null)
            {
                var uriBuilder = new UriBuilder(_url[type]);
                var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                query["version"] = data.GetValueOrDefault("version", "");
                query["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                uriBuilder.Query = query.ToString();
                request.RequestUri = uriBuilder.Uri;
            }

            res = await _httpClient.SendAsync(request);
            var content = await res.Content.ReadAsStringAsync();
            return JObject.Parse(content);
        }
        catch (Exception e)
        {
            return JObject.Parse($"{{\"error\":\"[{e.GetType().Name}] {type} 接口请求错误\",\"retcode\":1}}");
        }
    }

    private async Task<string> GetActIdAsync(string id)
    {
        var ret = await GetDataAsync($"act_id_{id}");
        if (ret == null) return "";

        // 检查error或retcode != 0
        if (ret["error"] != null || ret["retcode"]?.Value<int>() != 0)
            return "";

        string actId = "";
        var keywords = new List<string> { "前瞻特别节目" };

        var list = ret["data"]?["list"] as JArray;
        if (list == null) return "";

        foreach (var p in list)
        {
            var post = p["post"]?["post"];
            if (post == null) continue;

            var subject = post["subject"]?.Value<string>();
            if (string.IsNullOrEmpty(subject)) continue;

            bool containsAll = keywords.All(word => subject.Contains(word));
            if (!containsAll) continue;

            var structContent = post["structured_content"]?.Value<string>();
            if (string.IsNullOrEmpty(structContent)) continue;

            var segments = JArray.Parse(structContent);
            foreach (var segment in segments)
            {
                var link = segment["attributes"]?["link"]?.Value<string>() ?? "";
                // 优化：安全获取insert字段值
                string insert = "";
                var insertToken = segment["insert"];
                if (insertToken != null)
                {
                    if (insertToken.Type == JTokenType.String)
                    {
                        insert = insertToken.Value<string>() ?? "";
                    }
                    else if (insertToken.Type == JTokenType.Object)
                    {
                        // 如果是对象，尝试转换为字符串
                        insert = insertToken.ToString();
                    }
                }
                if ((insert.Contains("观看") || insert.Contains("米游社直播间")) && !string.IsNullOrEmpty(link))
                {
                    var match = Regex.Match(link, @"act_id=(.*?)\&");
                    if (match.Success)
                        actId = match.Groups[1].Value;
                }
            }

            if (!string.IsNullOrEmpty(actId)) break;
        }

        return actId;
    }

    private async Task<(string codeVer, string title)> GetLiveDataAsync(string actId)
    {
        var ret = await GetDataAsync("index", new Dictionary<string, string> { { "actId", actId } });
        if (ret == null || ret["error"] != null || ret["retcode"]?.Value<int>() != 0)
            return (null, null);

        var liveRaw = ret["data"]?["live"];
        if (liveRaw == null) return (null, null);

        string codeVer = liveRaw["code_ver"]?.Value<string>();
        string title = liveRaw["title"]?.Value<string>();
        return (codeVer, title);
    }

    private async Task<List<RedeemCode>> GetCodeAsync(string version, string actId)
    {
        var ret = await GetDataAsync("code", new Dictionary<string, string> { { "version", version }, { "actId", actId } });
        var result = new List<RedeemCode>();
        if (ret == null || ret["error"] != null || ret["retcode"]?.Value<int>() != 0)
            return result;

        var removeTag = new Regex("<.*?>", RegexOptions.Compiled);
        var codeList = ret["data"]?["code_list"] as JArray;
        if (codeList == null) return result;

        foreach (var codeInfo in codeList)
        {
            string items = removeTag.Replace(codeInfo["title"]?.Value<string>() ?? "", "");
            string code = codeInfo["code"]?.Value<string>();
            if (!string.IsNullOrEmpty(code))
                result.Add(new RedeemCode(code, items));
        }

        return result;
    }

    /// <summary>
    /// 获取前瞻直播兑换码信息。返回格式：List[("奖励内容", "兑换码")]
    /// </summary>
    public async Task<List<RedeemCode>> GetCodeMsgAsync()
    {
        string actId = await GetActIdAsync("1");
        if (string.IsNullOrEmpty(actId))
        {
            actId = await GetActIdAsync("2");
            if (string.IsNullOrEmpty(actId))
                throw new Exception("暂无前瞻直播资讯！");
        }

        var (codeVer, title) = await GetLiveDataAsync(actId);
        if (string.IsNullOrEmpty(codeVer))
            throw new Exception("前瞻直播数据异常");
        var codeList = await GetCodeAsync(codeVer, actId);
        return codeList;
    }
}
