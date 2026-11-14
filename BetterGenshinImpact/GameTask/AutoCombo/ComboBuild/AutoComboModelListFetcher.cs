using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>鉴权失败（401/403）：密钥不对，换候选地址没有意义，应直接停。</summary>
    private sealed class ModelListAuthException : Exception
    {
        public ModelListAuthException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// 按服务商拉取模型名列表。失败时抛出带可读原因的异常，由调用方展示。
    ///
    /// 返回的 <c>CorrectedEndpoint</c>：OpenAI 兼容地址成功命中后给出的规范服务地址
    /// （已去掉 /models 后缀），供设置页回写输入框；Anthropic / 发鱼站为 null（不回写）。
    /// </summary>
    public static async Task<(List<string> Models, string? CorrectedEndpoint)> FetchAsync(
        string provider,
        string endpointText,
        string apiKey,
        CancellationToken ct)
    {
        var normalized = AutoComboLlmProvider.Normalize(provider);

        Uri? endpoint;
        bool isLoopback;
        try
        {
            endpoint = AutoComboLlmEndpoint.Resolve(endpointText, normalized, out isLoopback);
        }
        catch (Exception e)
        {
            throw new Exception($"配置错误：{e.Message}", e);
        }

        // 与建树保持同一口径：发鱼站与本机回环地址不要求密钥，其余地址缺密钥只会拿到一个 401
        if (string.IsNullOrWhiteSpace(apiKey) && AutoComboLlmEndpoint.RequiresApiKey(normalized, isLoopback))
        {
            throw new Exception("配置错误：请先在任务设置页的“自动连招”卡片中配置 LLM 密钥（本机回环地址与发鱼站除外）");
        }

        if (normalized == AutoComboLlmProvider.Anthropic)
        {
            // Anthropic 的模型列表固定在服务根地址 /v1/models，无需候选尝试，也不回写地址。
            var anthropicUri = BuildAnthropicModelListUri(endpoint);
            var (anthropicModels, _) = await FetchModelsAsync(anthropicUri, normalized, apiKey, ct);
            if (anthropicModels.Count == 0)
            {
                throw new Exception($"服务端未返回任何模型：GET {DescribeUri(anthropicUri)}。");
            }

            return (anthropicModels, null);
        }

        if (normalized == AutoComboLlmProvider.FishRelief)
        {
            // 发鱼站用内置网关地址，模型列表固定在 /models，同样不回写地址。
            var fishReliefUri = new Uri($"{endpoint!.ToString().TrimEnd('/')}/models");
            var (fishReliefModels, _) = await FetchModelsAsync(fishReliefUri, normalized, apiKey, ct);
            if (fishReliefModels.Count == 0)
            {
                throw new Exception($"服务端未返回任何模型：GET {DescribeUri(fishReliefUri)}。");
            }

            return (fishReliefModels, null);
        }

        // OpenAI 兼容：用户填的地址可能只是纯域名，也可能已经带了 /v1 甚至 /models。
        // 逐个候选尝试，直到某个地址返回非空模型列表；成功后把规范地址（不含 /models）回写。
        var inputHasPath = endpoint!.AbsolutePath.Trim('/').Length > 0;
        var candidates = BuildOpenAiCandidates(endpoint);
        Exception? lastError = null;
        string? authError = null;
        foreach (var (candidate, isInputDerived) in candidates)
        {
            try
            {
                var (models, effectiveUri) = await FetchModelsAsync(candidate, normalized, apiKey, ct);
                if (models.Count > 0)
                {
                    // 只有「输入派生的候选」或「纯域名输入」才回写规范地址：
                    // 带子路径的输入若靠根级候选才拉到列表，说明用户配的路径没命中，不回写以免覆盖他的路径。
                    // 用实际生效的地址反推：候选地址可能被同源重定向改写，不能用候选地址本身。
                    var correctedEndpoint = isInputDerived || !inputHasPath ? ToBaseUrl(effectiveUri) : null;
                    return (models, correctedEndpoint);
                }

                lastError = new Exception($"该地址未返回任何模型：GET {DescribeUri(candidate)}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 取消要原样抛给调用方，不能当成「这个候选失败、换下一个」。
                throw;
            }
            catch (ModelListAuthException e)
            {
                // 鉴权失败也可能只是「这个地址不做校验、换到 /models 就正常」，所以继续试下一个；
                // 但鉴权原因是最有用的线索，单独记下，全部失败时优先报它。所有候选同源，不会把密钥发往别处。
                authError ??= e.Message;
                lastError = e;
            }
            catch (Exception e)
            {
                lastError = e;
            }
        }

        throw new Exception(
            $"已尝试 {candidates.Count} 个候选地址，均未获取到模型列表。原因：{authError ?? lastError?.Message ?? "未知"}");
    }

    /// <summary>
    /// 从单个模型列表地址拉取全部模型（含分页）。返回模型列表与**第一页实际生效的地址**（重定向可能改写地址）。
    /// 列表可能为空：「空」由调用方判断，单地址服务商算失败，OpenAI 兼容的候选尝试则换下一个。
    /// </summary>
    private static async Task<(List<string> Ids, Uri EffectiveUri)> FetchModelsAsync(
        Uri uri,
        string provider,
        string apiKey,
        CancellationToken ct)
    {
        var ids = new List<string>();
        var firstPageUri = uri;

        // 地址里已经把 limit 设到上限，正常情况下第一页就取完；这里再按 last_id 兜底翻页。
        // OpenAI 兼容端点不返回 has_more，会被当作只有一页。
        for (var page = 0; page < MaxModelListPages; page++)
        {
            var (effectiveUri, body) = await GetAsync(uri, provider, apiKey, ct);
            if (page == 0)
            {
                firstPageUri = effectiveUri;
            }

            var (pageIds, hasMore, lastId) = ParseModelPage(uri, body);

            foreach (var id in pageIds)
            {
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            if (!hasMore)
            {
                return (ids, firstPageUri);
            }

            if (string.IsNullOrWhiteSpace(lastId))
            {
                throw new Exception($"服务端返回 has_more=true 但未给出 last_id，无法继续取下一页：GET {DescribeUri(uri)}");
            }

            // 以实际生效的地址为基准翻页：重定向可能改写地址，甚至把查询串整个丢掉
            uri = BuildPageUri(effectiveUri, provider, lastId);
        }

        throw new Exception($"模型列表分页超过 {MaxModelListPages} 页仍未取完，已停止：GET {DescribeUri(uri)}");
    }

    /// <summary>Anthropic 的模型列表地址：固定在服务根地址下的 /v1/models。</summary>
    private static Uri BuildAnthropicModelListUri(Uri? endpoint)
    {
        return new Uri($"{AutoComboLlmEndpoint.ResolveAnthropicBaseUrl(endpoint)}/v1/models?limit={AnthropicModelListPageSize}");
    }

    /// <summary>
    /// 生成 OpenAI 兼容端点的候选模型列表地址，顺序：输入原文 → 输入（去尾斜杠）/models → 域名/models → 域名/v1/models。
    /// 「输入 + /models」排在域名级候选之前：用户配置了子路径（如 /team/v1）时先按他的路径试，
    /// 避免域名级路由碰巧能返回列表时把用户配置的路径覆盖掉。逐项去重。
    /// 前两项为「输入派生」，后两项为「根级回退」——调用方据此决定是否回写服务地址。
    /// </summary>
    private static List<(Uri Uri, bool IsInputDerived)> BuildOpenAiCandidates(Uri endpoint)
    {
        var origin = new Uri($"{endpoint.Scheme}://{endpoint.Authority}");

        // 把 /models 接到路径上而不是整串地址上：端点可能带查询参数（如 api-version），
        // 直接拼字符串会把 /models 拼到查询串后面，产生畸形地址。
        var inputWithModels = new UriBuilder(endpoint)
        {
            Path = endpoint.AbsolutePath.TrimEnd('/') + "/models",
        }.Uri;

        var candidates = new List<(Uri Value, bool IsInputDerived)>();
        void Add(Uri value, bool isInputDerived)
        {
            var key = value.ToString();
            if (!candidates.Any(c => c.Value.ToString() == key))
            {
                candidates.Add((value, isInputDerived));
            }
        }

        Add(endpoint, true);
        Add(inputWithModels, true);
        Add(new Uri($"{origin.ToString().TrimEnd('/')}/models"), false);
        Add(new Uri($"{origin.ToString().TrimEnd('/')}/v1/models"), false);

        return candidates;
    }

    /// <summary>
    /// 把命中模型列表的地址还原成规范服务地址：只去掉路径结尾的 /models，
    /// 查询串与片段原样保留——带查询参数的服务地址（如 api-version）在后续请求里同样需要。
    /// </summary>
    private static string ToBaseUrl(Uri modelsUri)
    {
        var path = modelsUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/models".Length];
        }

        var builder = new UriBuilder(modelsUri)
        {
            Path = path,
        };

        return builder.Uri.ToString();
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
            HttpResponseMessage response;
            try
            {
                using var request = BuildRequest(provider, uri, apiKey);
                response = await HttpClient.SendAsync(request, ct);
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                throw new Exception($"网络错误：请求超时（{HttpClient.Timeout.TotalSeconds:0} 秒未收到响应）：GET {DescribeUri(uri)}", e);
            }
            catch (HttpRequestException e)
            {
                throw new Exception($"网络错误：无法连接模型列表接口：GET {DescribeUri(uri)}：{e.Message}。请检查“LLM服务地址”、网络与代理设置", e);
            }

            string body;
            using (response)
            {
                try
                {
                    body = await response.Content.ReadAsStringAsync(ct);
                }
                catch (IOException e)
                {
                    throw new Exception($"网络错误：读取模型列表响应失败：GET {DescribeUri(uri)}：{e.Message}", e);
                }

                // 自动重定向是关的，3xx 会落到这里：同源跳转自己跟，跨源一律拒绝
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    uri = ResolveRedirect(uri, response, redirects);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var failureMessage = BuildHttpFailureMessage(uri, response, body);

                    // 鉴权失败是密钥不对，换地址没有意义：用专用异常让候选循环直接停。
                    if ((int)response.StatusCode is 401 or 403)
                    {
                        throw new ModelListAuthException(failureMessage);
                    }

                    throw new Exception(failureMessage);
                }

                return (uri, body);
            }
        }
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
            throw new Exception($"服务连续重定向超过 {MaxSameAuthorityRedirects} 次（最后一次 HTTP {status}）：GET {DescribeUri(current)}，已停止");
        }

        if (location is null)
        {
            throw new Exception($"服务返回 HTTP {status} 但未给出 Location，无法继续：GET {DescribeUri(current)}");
        }

        var target = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (current.Scheme != target.Scheme || current.Host != target.Host || current.Port != target.Port)
        {
            throw new Exception(
                $"配置错误：服务返回了跨域重定向（HTTP {status} → {DescribeUri(target)}）。为避免密钥被转发到其他地址，这里不会跟随；" +
                "请确认「LLM服务地址」填写的是该服务的地址。");
        }

        return target;
    }

    /// <summary>
    /// 按状态码生成可操作的失败提示：鉴权、地址配置、服务端故障各给出不同排查方向。
    /// 提示里回显的地址已去掉查询串与用户信息，避免把可能含凭证的部分带入 UI 或日志。
    /// </summary>
    private static string BuildHttpFailureMessage(Uri uri, HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        var category = status switch
        {
            401 or 403 => "鉴权失败",
            404 => "配置错误",
            >= 500 => "服务端错误",
            _ => "服务端拒绝请求",
        };
        var hint = status switch
        {
            401 or 403 => "请检查 LLM 密钥是否正确；发鱼站通道无需密钥",
            404 => "接口不存在，请检查“LLM服务地址”，OpenAI 兼容地址通常需要以 /v1 结尾",
            >= 500 => "服务端暂时不可用，请稍后重试或联系服务提供方",
            _ => "请检查服务商类型、服务地址以及服务端日志",
        };

        return $"{category}：GET {DescribeUri(uri)} → HTTP {status} {response.ReasonPhrase}；{hint}。响应摘要：{Summarize(body)}";
    }

    /// <summary>
    /// 只保留协议、主机、端口与路径，丢掉查询串和用户信息，避免把可能含凭证的部分写进提示或日志。
    /// </summary>
    private static string DescribeUri(Uri uri)
    {
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}";
    }

    /// <summary>
    /// 解析一页列表响应：<c>{"data":[{"id":"..."}],"has_more":bool,"last_id":"..."}</c>。
    /// OpenAI 兼容端点不返回 has_more / last_id，取默认值即可（视为只有一页）。
    /// </summary>
    private static (List<string> Ids, bool HasMore, string? LastId) ParseModelPage(Uri uri, string body)
    {
        JObject root;
        try
        {
            root = JObject.Parse(body);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new Exception($"服务端返回无法解析的模型列表响应：GET {DescribeUri(uri)}：{Summarize(body)}", e);
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
