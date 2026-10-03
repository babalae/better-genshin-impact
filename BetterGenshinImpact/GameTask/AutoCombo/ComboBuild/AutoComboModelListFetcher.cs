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

        var request = normalized == AutoComboLlmProvider.Anthropic
            ? BuildAnthropicRequest(endpoint, apiKey)
            : BuildOpenAiCompatibleRequest(endpoint!, apiKey);

        using var response = await HttpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // 上面关掉了自动重定向，所以 3xx 会走到这里：给出可执行的提示，而不是让用户对着一个空的 302 发愣
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new Exception(
                $"服务返回了重定向（HTTP {(int)response.StatusCode} → {response.Headers.Location}）。" +
                "为避免密钥被转发到其他地址，这里不会自动跟随；请把服务地址直接填写为重定向后的地址。");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}：{Summarize(body)}");
        }

        return ParseModelIds(body);
    }

    /// <summary>
    /// OpenAI 兼容端点：模型列表与对话接口同层级（端点通常已含 /v1，这里只追加 /models）。
    /// </summary>
    private static HttpRequestMessage BuildOpenAiCompatibleRequest(Uri endpoint, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.ToString().TrimEnd('/')}/models");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    /// <summary>
    /// Anthropic：模型列表在服务根地址下的 /v1/models，鉴权用 x-api-key。
    /// </summary>
    private static HttpRequestMessage BuildAnthropicRequest(Uri? endpoint, string apiKey)
    {
        var baseUrl = AutoComboLlmEndpoint.ResolveAnthropicBaseUrl(endpoint);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicApiVersion);
        return request;
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
