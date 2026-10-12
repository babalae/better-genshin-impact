using System;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 自动连招所用服务地址的解析与校验。
///
/// 建树客户端与模型列表拉取共用同一份判定，避免两处对「地址能不能用」给出不同结论。
/// </summary>
public static class AutoComboLlmEndpoint
{
    /// <summary>
    /// Anthropic 官方生产环境地址。
    /// 这里不沿用 SDK 的 ANTHROPIC_BASE_URL 环境变量回退：否则用户机器上的环境变量会在界面上看不出来的情况下
    /// 决定密钥发往哪个地址。
    /// </summary>
    public const string AnthropicDefaultBaseUrl = "https://api.anthropic.com";

    /// <summary>
    /// 解析并校验服务地址。
    /// OpenAI 兼容端点必填；Anthropic 允许留空（返回 null，表示使用官方地址）。
    /// 地址非 HTTPS 且不是本机回环时直接报错：密钥会随每个请求明文发出。
    /// </summary>
    public static Uri? Resolve(string endpointText, string provider, out bool isLoopback)
    {
        isLoopback = false;
        if (string.IsNullOrWhiteSpace(endpointText))
        {
            if (provider == AutoComboLlmProvider.OpenAiCompatible)
            {
                throw new Exception("请先在任务设置页的“自动连招”卡片中配置 LLM 服务地址");
            }

            return null;
        }

        Uri endpoint;
        try
        {
            endpoint = new Uri(endpointText.Trim());
        }
        catch (UriFormatException e)
        {
            throw new Exception($"LLM 服务地址无效：{endpointText}", e);
        }

        isLoopback = endpoint.Host is "localhost" or "127.0.0.1" or "::1" || endpoint.Host.StartsWith("[::1]");
        if (endpoint.Scheme != Uri.UriSchemeHttps && !isLoopback)
        {
            throw new Exception($"LLM 服务地址必须使用 HTTPS（否则密钥将明文传输），本机回环地址除外：{endpointText}");
        }

        return endpoint;
    }

    /// <summary>
    /// 取 Anthropic 的服务根地址：留空时用官方地址，填写时去掉结尾斜杠，
    /// 避免与 /v1/... 路径自行拼接时组成双斜杠。
    /// </summary>
    public static string ResolveAnthropicBaseUrl(Uri? endpoint)
    {
        return endpoint is null
            ? AnthropicDefaultBaseUrl
            : $"{endpoint.Scheme}://{endpoint.Authority}{endpoint.AbsolutePath.TrimEnd('/')}";
    }
}
