using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>VOICEVOX 官方两阶段HTTP协议；不安装模型或启动服务。</summary>
public sealed class VoicevoxTtsProvider(Func<TimeoutSettings, HttpClient> clientFactory) : HttpSpeechProvider(clientFactory)
{
    /// <summary>独立能力描述。</summary>
    public override TtsProviderInfo Info => TtsProviderRegistry.GetInfo("voicevox");
    /// <summary>style ID和官方普通语音参数范围。</summary>
    protected override void ValidateVoice(AppSettings settings)
    {
        var extra = settings.Extra ?? new();
        if (!int.TryParse(settings.Provider?.VoiceId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 0)
            throw Configuration("VOICEVOX 声音必须为有效的 style ID。");
        if (!double.IsFinite(extra.Rate) || extra.Rate is < .5 or > 2 || !double.IsFinite(extra.Pitch) || extra.Pitch is < -.15 or > .15)
            throw Configuration("VOICEVOX 速度应为 0.5～2，音高应为 -0.15～0.15。");
    }
    /// <summary>区分speaker展示名和真正合成用的style ID。</summary>
    protected override async Task<IReadOnlyList<TtsVoiceInfo>> ReadVoicesAsync(AppSettings settings, HttpClient client, CancellationToken token)
    {
        using var json = await JsonAsync(client, new(HttpMethod.Get, Endpoint(settings, "speakers")), token).ConfigureAwait(false);
        var result = new List<TtsVoiceInfo>();
        foreach (var speaker in json.RootElement.EnumerateArray())
            foreach (var style in speaker.GetProperty("styles").EnumerateArray())
                if (!style.TryGetProperty("type", out var type) || type.GetString() == "talk")
                    result.Add(new(style.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture),
                        speaker.GetProperty("name").GetString() + " · " + style.GetProperty("name").GetString(), "ja"));
        return result.AsReadOnly();
    }
    /// <summary>完整保留查询中的accent/音素字段，仅替换用户可调参数。</summary>
    protected override async Task<(WavAudio, TtsAudioEncoding)> ReadSpeechAsync(AppSettings settings, TtsRequest request, HttpClient client, CancellationToken token)
    {
        var speaker = settings.Provider!.VoiceId;
        var query = await SendAsync(client, new(HttpMethod.Post, Endpoint(settings, "audio_query?speaker=" + speaker + "&text=" + Uri.EscapeDataString(request.Text))), token, 2 * 1024 * 1024).ConfigureAwait(false);
        var body = JsonNode.Parse(query) as JsonObject ?? throw Invalid("VOICEVOX audio_query未返回对象。");
        body["speedScale"] = settings.Extra?.Rate ?? 1;
        body["pitchScale"] = settings.Extra?.Pitch ?? 0;
        var audio = await SendAsync(client, new(HttpMethod.Post, Endpoint(settings, "synthesis?speaker=" + speaker))
            { Content = JsonContent.Create(body) }, token).ConfigureAwait(false);
        return (await DecodeAsync(audio, TtsAudioEncoding.Pcm16Wav, token).ConfigureAwait(false), TtsAudioEncoding.Pcm16Wav);
    }
}
