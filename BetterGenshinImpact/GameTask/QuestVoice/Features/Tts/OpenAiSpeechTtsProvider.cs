using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using NAudio.Wave;
using System.IO;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>OpenAI兼容 audio/speech；不假设第三方必须提供账号或模型列表。</summary>
public sealed class OpenAiSpeechTtsProvider(Func<TimeoutSettings, HttpClient> clientFactory) : HttpSpeechProvider(clientFactory)
{
    /// <summary>独立能力描述。</summary>
    public override TtsProviderInfo Info => TtsProviderRegistry.GetInfo("openai-speech");
    /// <summary>官方内置声音作为建议；兼容服务的自定义ID可以手输。</summary>
    private static readonly string[] VoiceIds = ["alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar"];
    /// <summary>兼容服务无统一只读探测协议，避免把静态列表宣称为连接成功。</summary>
    public override Task<string> ProbeAsync(AppSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Validate(settings);
        return Task.FromResult("OpenAI兼容配置格式有效；内置声音仅供建议，连接/认证/推理请使用测试语音。");
    }
    /// <summary>只接受已有解码器支持的格式，PCM协议为24kHz单声道小端16位。</summary>
    protected override void ValidateVoice(AppSettings settings)
    {
        var extra = settings.Extra ?? new();
        if (string.IsNullOrWhiteSpace(settings.Provider?.VoiceId) || string.IsNullOrWhiteSpace(extra.Model)) throw Configuration("请填写模型与 voice ID。");
        if (extra.Format is not ("wav" or "mp3" or "pcm") || !double.IsFinite(extra.Rate) || extra.Rate is < .25 or > 4)
            throw Configuration("音频格式应为 wav/mp3/pcm，速度应为 0.25～4。");
    }
    /// <summary>没有标准voice枚举端点，明确返回建议值而不虚构网络探测。</summary>
    protected override Task<IReadOnlyList<TtsVoiceInfo>> ReadVoicesAsync(AppSettings settings, HttpClient client, CancellationToken token)
    {
        var result = new List<TtsVoiceInfo>();
        foreach (var id in VoiceIds)
        {
            if ((settings.Extra?.Model is "tts-1" or "tts-1-hd") && (id is "ballad" or "verse" or "marin" or "cedar")) continue;
            result.Add(new(id, id + " · 内置建议", "multilingual"));
        }
        return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(result.AsReadOnly());
    }
    /// <summary>发送可选Bearer认证，错误响应内容不回显。</summary>
    protected override async Task<(WavAudio, TtsAudioEncoding)> ReadSpeechAsync(AppSettings settings, TtsRequest request, HttpClient client, CancellationToken token)
    {
        var extra = settings.Extra ?? new();
        var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings, "audio/speech"))
        { Content = JsonContent.Create(new { model = extra.Model, input = request.Text, voice = settings.Provider!.VoiceId, speed = extra.Rate, response_format = extra.Format }) };
        if (!string.IsNullOrEmpty(extra.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", extra.ApiKey);
        var bytes = await SendAsync(client, message, token).ConfigureAwait(false);
        if (extra.Format == "pcm")
        {
            if (bytes.Length == 0 || bytes.Length % 2 != 0) throw Invalid("PCM响应为空或包含不完整采样。");
            using var output = new MemoryStream();
            using (var writer = new WaveFileWriter(output, new WaveFormat(24000, 16, 1))) { writer.Write(bytes, 0, bytes.Length); writer.Flush(); }
            bytes = output.ToArray();
        }
        var encoding = extra.Format == "mp3" ? TtsAudioEncoding.Mp3 : TtsAudioEncoding.Pcm16Wav;
        return (await DecodeAsync(bytes, encoding, token).ConfigureAwait(false), extra.Format == "pcm" ? TtsAudioEncoding.Pcm16 : encoding);
    }
}
