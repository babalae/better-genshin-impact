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
/// 两个服务商的列表接口都返回 <c>{"data":[{"id":"..."}]}</c>，因此共用同一套解析。
/// </summary>
public static class AutoComboModelListFetcher
{
    /// <summary>Anthropic 要求显式携带版本头。</summary>
    private const string AnthropicApiVersion = "2023-06-01";

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
        for (var redirects = 0; ; redirects++)
        {
            using var request = BuildRequest(normalized, uri, apiKey);
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

            return ParseModelIds(body);
        }
    }

    /// <summary>
    /// 拼出模型列表请求地址。
    /// OpenAI 兼容端点通常已含 /v1，这里只追加 /models；Anthropic 的列表固定在服务根地址下的 /v1/models。
    /// </summary>
    private static Uri BuildModelListUri(string provider, Uri? endpoint)
    {
        return provider == AutoComboLlmProvider.Anthropic
            ? new Uri($"{AutoComboLlmEndpoint.ResolveAnthropicBaseUrl(endpoint)}/v1/models")
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
    /// 解析 <c>{"data":[{"id":"..."}]}</c> 形式的响应，按出现顺序返回去重后的模型名。
    /// 空列表或结构不符时抛错，交由调用方提示。
    /// </summary>
    private static List<string> ParseModelIds(string body)
    {
        JToken? data;
        try
        {
            data = JObject.Parse(body)["data"];
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new Exception($"无法解析模型列表响应：{Summarize(body)}", e);
        }

        var ids = new List<string>();
        if (data is JArray array)
        {
            foreach (var item in array)
            {
                var id = item["id"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
        }

        if (ids.Count == 0)
        {
            throw new Exception("服务返回的模型列表为空");
        }

        return ids;
    }

    /// <summary>把响应正文压成单行并截断，用于错误提示。</summary>
    private static string Summarize(string body)
    {
        var text = body.Trim().ReplaceLineEndings(" ");
        return text.Length <= MaxEchoLength ? text : text[..MaxEchoLength] + "…";
    }
}
