using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 拉取所选服务商的可用模型列表，供设置页下拉选择。只读查询，不修改任何配置。
///
/// 两个服务商的列表接口都返回 <c>{"data":[{"id":"..."}]}</c>，因此共用同一套解析；
/// Anthropic 额外带 <c>has_more</c> / <c>last_id</c> 分页字段，需要逐页取完。
/// </summary>
public static class AutoComboModelListFetcher
{
    /// <summary>Anthropic 要求显式携带版本头。</summary>
    private const string AnthropicApiVersion = "2023-06-01";

    /// <summary>Anthropic 列表接口的每页条数上限（接口文档：取值 1~1000，默认为 20）。</summary>
    private const int AnthropicModelListPageSize = 1000;

    /// <summary>分页的最大页数，避免服务端始终返回 has_more=true 时无限翻页。</summary>
    private const int MaxModelListPages = 5;

    /// <summary>错误提示里回显的响应正文上限，避免把整段响应贴进 UI。</summary>
    private const int MaxEchoLength = 300;

    /// <summary>同源重定向的最大跟随次数，避免服务端构造重定向环。</summary>
    private const int MaxSameAuthorityRedirects = 3;

    /// <summary>
    /// 拉取模型列表专用的 HttpClient：不跟随重定向。
    ///
    /// x-api-key 是自定义头，而 .NET 的自动重定向只会清理 Authorization、不会清理它，
    /// 服务端一旦返回 3xx 指向其他地址，密钥就会被一并转发过去。
    /// </summary>
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    /// <summary>
    /// 按服务商拉取模型名列表。失败时抛出带可读原因的异常，由调用方展示。
    /// </summary>
    public static async Task<List<string>> FetchAsync(string provider, string endpointText, string apiKey, CancellationToken ct)
    {
        var normalized = AutoComboLlmProvider.Normalize(provider);
        var endpoint = AutoComboLlmEndpoint.Resolve(endpointText, normalized, out var isLoopback);

        // 与建树保持同一口径：非本机回环地址必须配置密钥，否则这里只会拿到一个 401
        if (string.IsNullOrWhiteSpace(apiKey) && !isLoopback)
        {
            throw new Exception("请先在任务设置页的“自动连招”卡片中配置 LLM 密钥（本机回环地址除外）");
        }

        var uri = BuildModelListUri(normalized, endpoint);
        var ids = new List<string>();

        // 地址里已经把 limit 设到上限，正常情况下第一页就取完；这里再按 last_id 兜底翻页。
        // OpenAI 兼容端点不返回 has_more，会被当作只有一页。
        for (var page = 0; page < MaxModelListPages; page++)
        {
            var (effectiveUri, body) = await GetAsync(uri, normalized, apiKey, ct);
            var (pageIds, hasMore, lastId) = ParseModelPage(body);

            foreach (var id in pageIds)
            {
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            if (!hasMore)
            {
                if (ids.Count == 0)
                {
                    throw new Exception("服务返回的模型列表为空");
                }

                return ids;
            }

            if (string.IsNullOrWhiteSpace(lastId))
            {
                throw new Exception("服务返回 has_more=true 但未给出 last_id，无法继续取下一页");
            }

            // 以实际生效的地址为基准翻页：重定向可能改写地址，甚至把查询串整个丢掉
            uri = BuildPageUri(effectiveUri, normalized, lastId);
        }

        throw new Exception($"模型列表分页超过 {MaxModelListPages} 页仍未取完，已停止");
    }

    /// <summary>
    /// 发一次 GET，返回响应正文与**实际生效的地址**。
    /// 自动重定向是关的，这里自行跟随同源跳转（最多 <see cref="MaxSameAuthorityRedirects"/> 次），
    /// 跨源一律拒绝：那等于把 x-api-key 交给另一台服务器。
    /// 必须把最终地址交回调用方：否则分页游标会拼在重定向前的地址上，被服务端再次丢掉。
    /// </summary>
    private static async Task<(Uri EffectiveUri, string Body)> GetAsync(Uri uri, string provider, string apiKey, CancellationToken ct)
    {
        for (var redirects = 0; ; redirects++)
        {
            using var request = BuildRequest(provider, uri, apiKey);
            using var response = await HttpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            // 自动重定向是关的，3xx 会落到这里：同源跳转自己跟，跨源一律拒绝
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                uri = ResolveRedirect(uri, response, redirects);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}：{Summarize(body)}");
            }

            return (uri, body);
        }
    }

    /// <summary>
    /// 拼出模型列表请求地址。
    /// OpenAI 兼容端点通常已含 /v1，这里只追加 /models；Anthropic 的列表固定在服务根地址下的 /v1/models。
    /// </summary>
    private static Uri BuildModelListUri(string provider, Uri? endpoint)
    {
        return provider == AutoComboLlmProvider.Anthropic
            ? new Uri($"{AutoComboLlmEndpoint.ResolveAnthropicBaseUrl(endpoint)}/v1/models?limit={AnthropicModelListPageSize}")
            : new Uri($"{endpoint!.ToString().TrimEnd('/')}/models");
    }

    /// <summary>
    /// 构造带鉴权头的 GET 请求。
    /// 跟随重定向后要用新地址重建，所以这里每次循环都重新构造，不能复用同一个 HttpRequestMessage。
    /// </summary>
    private static HttpRequestMessage BuildRequest(string provider, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (provider == AutoComboLlmProvider.Anthropic)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicApiVersion);
        }
        else if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    /// <summary>
    /// 计算可安全跟随的重定向目标。只有协议、主机、端口三者都一致才跟：
    /// 跨源跳转等于把 x-api-key 交给另一台服务器，那不是用户配置的那台。
    /// </summary>
    private static Uri ResolveRedirect(Uri current, HttpResponseMessage response, int redirects)
    {
        var status = (int)response.StatusCode;
        var location = response.Headers.Location;

        if (redirects >= MaxSameAuthorityRedirects)
        {
            throw new Exception($"服务连续重定向超过 {MaxSameAuthorityRedirects} 次（最后一次 HTTP {status} → {location}），已停止");
        }

        if (location is null)
        {
            throw new Exception($"服务返回 HTTP {status} 但未给出 Location，无法继续");
        }

        var target = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (current.Scheme != target.Scheme || current.Host != target.Host || current.Port != target.Port)
        {
            throw new Exception(
                $"服务返回了跨域重定向（HTTP {status} → {target}）。为避免密钥被转发到其他地址，这里不会跟随；" +
                "请确认「LLM服务地址」填写的是该服务的地址。");
        }

        return target;
    }

    /// <summary>
    /// 解析一页列表响应：<c>{"data":[{"id":"..."}],"has_more":bool,"last_id":"..."}</c>。
    /// OpenAI 兼容端点不返回 has_more / last_id，取默认值即可（视为只有一页）。
    /// </summary>
    private static (List<string> Ids, bool HasMore, string? LastId) ParseModelPage(string body)
    {
        JObject root;
        try
        {
            root = JObject.Parse(body);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new Exception($"无法解析模型列表响应：{Summarize(body)}", e);
        }

        var ids = new List<string>();
        if (root["data"] is JArray array)
        {
            foreach (var item in array)
            {
                var id = item["id"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return (ids, root["has_more"]?.Value<bool>() ?? false, root["last_id"]?.Value<string>());
    }

    /// <summary>
    /// 以实际生效的地址为基准拼下一页的地址。
    /// 游标是替换而不是追加：若在上一页地址上直接追加，翻到第三页会出现两个 after_id。
    /// 重定向可能把整个查询串丢掉，所以 limit 也在这里重新补上。
    /// </summary>
    private static Uri BuildPageUri(Uri effectiveUri, string provider, string cursor)
    {
        var isAnthropic = provider == AutoComboLlmProvider.Anthropic;
        var builder = new UriBuilder(effectiveUri);
        var kept = new List<string>();

        // 保留地址里原有的其他查询参数，只替换我们自己管的这两个
        foreach (var pair in builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = pair.Split('=', 2)[0];
            if (key.Equals("after_id", StringComparison.OrdinalIgnoreCase)
                || (isAnthropic && key.Equals("limit", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            kept.Add(pair);
        }

        if (isAnthropic)
        {
            kept.Add($"limit={AnthropicModelListPageSize}");
        }

        kept.Add($"after_id={Uri.EscapeDataString(cursor)}");
        builder.Query = string.Join('&', kept);
        return builder.Uri;
    }

    /// <summary>把响应正文压成单行并截断，用于错误提示。</summary>
    private static string Summarize(string body)
    {
        var text = body.Trim().ReplaceLineEndings(" ");
        return text.Length <= MaxEchoLength ? text : text[..MaxEchoLength] + "…";
    }
}
