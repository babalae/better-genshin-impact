using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using NAudio.Wave;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>Google Translate公开网页TTS协议；非Cloud服务承诺接口，不绕过拒绝或认证。</summary>
public sealed class GoogleTranslateTtsProvider(Func<TimeoutSettings, HttpClient> clientFactory) : HttpSpeechProvider(clientFactory)
{
    /// <summary>协议固定在本适配器中，不影响宿主启动。</summary>
    public static Uri SpeechEndpoint { get; } = new("https://translate.google.com/_/TranslateWebserverUi/data/batchexecute");
    /// <summary>独立能力描述。</summary>
    public override TtsProviderInfo Info => TtsProviderRegistry.GetInfo("google-translate");
    /// <summary>有限语言预设，不将语言误表示为可选说话人。</summary>
    private static readonly IReadOnlyList<TtsVoiceInfo> Languages = Array.AsReadOnly(new[]
    {
        new TtsVoiceInfo("zh-CN", "中文（简体）", "zh-CN"), new TtsVoiceInfo("zh-TW", "中文（繁体）", "zh-TW"),
        new TtsVoiceInfo("en", "English", "en"), new TtsVoiceInfo("ja", "日本語", "ja"),
        new TtsVoiceInfo("ko", "한국어", "ko"), new TtsVoiceInfo("fr", "Français", "fr"), new TtsVoiceInfo("de", "Deutsch", "de")
    });
    /// <summary>仅提供实际协议支持的正常/慢速。</summary>
    protected override void ValidateVoice(AppSettings settings)
    {
        if (!Languages.Any(x => x.Id == settings.Provider?.VoiceId) || (settings.Extra?.Rate ?? 1) is not (1 or .5))
            throw Configuration("请选择Google语言，速度仅支持 1（正常）或 0.5（慢速）。");
    }
    /// <summary>语言列表不依赖网络。</summary>
    protected override Task<IReadOnlyList<TtsVoiceInfo>> ReadVoicesAsync(AppSettings settings, HttpClient client, CancellationToken token)
        => Task.FromResult(Languages);
    /// <summary>只读可达性检测不创建语音任务。</summary>
    public override async Task<string> ProbeAsync(AppSettings settings, CancellationToken token)
    {
        await CheckEndpointAsync(settings, SpeechEndpoint, token).ConfigureAwait(false);
        return "Google域名已响应；语言为本地建议列表，实际合成请测试语音。";
    }
    /// <summary>按Unicode文本元素切成最多100个；避免拆开代理对或组合字符。</summary>
    public static IReadOnlyList<string> SplitText(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var parts = new List<string>();
        for (var index = 0; index < starts.Length;)
        {
            var end = Math.Min(index + 100, starts.Length);
            // 同时限制UTF16长度，兼容服务器不同字符计数方式。
            while (end > index + 1 && (end == starts.Length ? text.Length : starts[end]) - starts[index] > 100) end--;
            parts.Add(text[starts[index]..(end == starts.Length ? text.Length : starts[end])]);
            index = end;
        }
        return parts;
    }
    /// <summary>每段单独解码MP3，再按相同PCM格式顺序拼接，不拼多个WAV首部。</summary>
    protected override async Task<(WavAudio, TtsAudioEncoding)> ReadSpeechAsync(AppSettings settings, TtsRequest request, HttpClient client, CancellationToken token)
    {
        using var output = new MemoryStream();
        WaveFileWriter? writer = null;
        try
        {
            foreach (var text in SplitText(request.Text))
            {
                // 不向服务发送纯空白/标点分段，避免无语音的合法响应中断整句。
                if (!text.Any(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsControl(c))) continue;
                var parameter = JsonSerializer.Serialize(new object?[] { text, settings.Provider!.VoiceId, settings.Extra?.Rate == .5 ? true : null, "null" });
                var rpc = JsonSerializer.Serialize(new object[] { new object[] { new object?[] { "jQ1olc", parameter, null, "generic" } } });
                var message = new HttpRequestMessage(HttpMethod.Post, SpeechEndpoint)
                    { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["f.req"] = rpc }) };
                var response = await SendAsync(client, message, token, 24 * 1024 * 1024).ConfigureAwait(false);
                var encoded = ExtractAudio(response);
                var audio = await DecodeAsync(encoded, TtsAudioEncoding.Mp3, token).ConfigureAwait(false);
                using var input = new WaveFileReader(new MemoryStream(audio.Bytes, false));
                writer ??= new WaveFileWriter(output, input.WaveFormat);
                if (!writer.WaveFormat.Equals(input.WaveFormat)) throw Invalid("Google分段返回了不同音频格式。");
                var block = new byte[16384];
                int count;
                while ((count = input.Read(block, 0, block.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (output.Length + count > TtsAudioDecoder.MaxDecodedBytes) throw Invalid("合并音频超出大小限制。");
                    writer.Write(block, 0, count);
                }
            }
            writer?.Flush();
        }
        finally { writer?.Dispose(); }
        return (new WavProcessor().ValidateCompletedResponse(output.ToArray()), TtsAudioEncoding.Mp3);
    }
    /// <summary>跳过RPC长度/防JSON劫持前缀，仅接受明确的jQ1olc结果。</summary>
    private static byte[] ExtractAudio(byte[] response)
    {
        foreach (var line in System.Text.Encoding.UTF8.GetString(response).Split('\n'))
        {
            if (!line.StartsWith('[')) continue;
            using var json = JsonDocument.Parse(line);
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 3 || item[1].GetString() != "jQ1olc") continue;
                using var audio = JsonDocument.Parse(item[2].GetString() ?? "null");
                if (audio.RootElement.ValueKind != JsonValueKind.Array || audio.RootElement.GetArrayLength() == 0
                    || audio.RootElement[0].ValueKind != JsonValueKind.String) throw Invalid("Google语音结果为空。");
                return audio.RootElement[0].GetBytesFromBase64();
            }
        }
        throw Invalid("Google未返回语音数据。");
    }
}
