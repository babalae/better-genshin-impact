using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>只补充普通 Provider 能力，切权、Unknown 和排空仍由原引擎拥有。</summary>
public sealed partial class GptSovitsEngine
{
    /// <summary>描述实际支持的完整和流式功能。</summary>
    public TtsProviderInfo Info => TtsProviderRegistry.GetInfo("gpt-sovits");
    /// <summary>拒绝非 HTTP 地址及带凭据/查询/片段的服务 URL。</summary>
    public void Validate(AppSettings settings)
    {
        var url = settings.TtsServiceUrl;
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || string.IsNullOrEmpty(url.Host)
            || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            throw new VoiceException(VoiceErrorCode.Configuration, "GPT-SoVITS 地址必须为完整 HTTP/HTTPS 服务地址，不带凭据、查询或片段。");
        if (settings.Timeouts.ConnectSeconds is < 1 or > 120 || settings.Timeouts.RequestSeconds is < 1 or > 600
            || settings.Timeouts.SynthesisSeconds is < 1 or > 600)
            throw new VoiceException(VoiceErrorCode.Configuration, "连接超时应为 1～120 秒，请求/合成超时应为 1～600 秒。");
    }
    /// <summary>api_v2 没有通用 voice list，返回当前已配置默认声线。</summary>
    public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(new[] { new TtsVoiceInfo("configured", settings.Voice.Name, "zh") });
    }
    /// <summary>只探测 docs，绝不因探测成功解除 Unknown 或发出切权。</summary>
    public async Task<string> ProbeAsync(AppSettings settings, CancellationToken token)
    {
        Validate(settings);
        using var client = clientFactory(settings.Timeouts);
        client.Timeout = TimeSpan.FromSeconds(settings.Timeouts.RequestSeconds);
        using var response = await client.GetAsync(new Uri(settings.TtsServiceUrl, "docs"),
            HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new VoiceException(VoiceErrorCode.Unreachable, $"GPT-SoVITS 连接检测失败：HTTP {(int)response.StatusCode}。");
        return $"HTTP {(int)response.StatusCode} 已响应；实际合成与后端空闲状态待验证。";
    }
}
