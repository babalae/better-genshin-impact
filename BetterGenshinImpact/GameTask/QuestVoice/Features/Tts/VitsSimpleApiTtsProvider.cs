using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>当前 vits-simple-api 的 VITS/BERT-VITS2 文本合成；不包含转换或GPT旁路。</summary>
public sealed class VitsSimpleApiTtsProvider(Func<TimeoutSettings, HttpClient> clientFactory) : HttpSpeechProvider(clientFactory)
{
    /// <summary>独立能力描述。</summary>
    public override TtsProviderInfo Info => TtsProviderRegistry.GetInfo("vits-simple-api");
    /// <summary>模型类型和ID共同组成稳定声线引用，避免不同模型id=0串绑。</summary>
    private static (string Model, int Id) ParseVoice(string? voice)
    {
        var parts = voice?.Split(':');
        if (parts?.Length != 2 || parts[0] is not ("VITS" or "BERT-VITS2")
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 0)
            throw Configuration("vits-simple-api 声音格式应为 VITS:0 或 BERT-VITS2:0。");
        return (parts[0], id);
    }
    /// <summary>只公布实现过的语言及速度，不猜测其他模型的必需参数。</summary>
    protected override void ValidateVoice(AppSettings settings)
    {
        ParseVoice(settings.Provider?.VoiceId);
        var extra = settings.Extra ?? new();
        if (extra.Language is not ("auto" or "zh" or "ja" or "en") || !double.IsFinite(extra.Rate) || extra.Rate is < .5 or > 2)
            throw Configuration("VITS语言应为 auto/zh/ja/en，速度应为 0.5～2。");
    }
    /// <summary>密钥仅在请求头，绝不拼进URL。</summary>
    private static HttpRequestMessage Authorize(AppSettings settings, HttpRequestMessage message)
    {
        if (!string.IsNullOrEmpty(settings.Extra?.ApiKey)) message.Headers.Add("X-API-KEY", settings.Extra.ApiKey);
        return message;
    }
    /// <summary>只枚举本客户端能合成的模型类型。</summary>
    protected override async Task<IReadOnlyList<TtsVoiceInfo>> ReadVoicesAsync(AppSettings settings, HttpClient client, CancellationToken token)
    {
        using var json = await JsonAsync(client, Authorize(settings, new(HttpMethod.Get, Endpoint(settings, "voice/speakers"))), token).ConfigureAwait(false);
        var result = new List<TtsVoiceInfo>();
        foreach (var group in json.RootElement.EnumerateObject())
        {
            if (group.Name is not ("VITS" or "BERT-VITS2")) continue;
            foreach (var item in group.Value.EnumerateArray())
            {
                var id = group.Name + ":" + item.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture);
                var languages = new List<string>();
                foreach (var language in item.GetProperty("lang").EnumerateArray()) languages.Add(language.GetString() ?? "");
                result.Add(new(id, group.Name + " · " + item.GetProperty("name").GetString(), string.Join('/', languages)));
            }
        }
        return result.AsReadOnly();
    }
    /// <summary>POST JSON 避免把对白写入代理URL日志，强制完整 WAV。</summary>
    protected override async Task<(WavAudio, TtsAudioEncoding)> ReadSpeechAsync(AppSettings settings, TtsRequest request, HttpClient client, CancellationToken token)
    {
        var (model, id) = ParseVoice(settings.Provider!.VoiceId);
        var extra = settings.Extra ?? new(Language: "auto");
        var message = Authorize(settings, new(HttpMethod.Post, Endpoint(settings, "voice/" + model.ToLowerInvariant()))
        { Content = JsonContent.Create(new { text = request.Text, id, lang = extra.Language, length = 1 / extra.Rate, format = "wav", streaming = false }) });
        // 上游当前按完整Content-Type字符串匹配JSON，不能附带默认charset。
        message.Content!.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var bytes = await SendAsync(client, message, token).ConfigureAwait(false);
        return (await DecodeAsync(bytes, TtsAudioEncoding.Pcm16Wav, token).ConfigureAwait(false), TtsAudioEncoding.Pcm16Wav);
    }
}
