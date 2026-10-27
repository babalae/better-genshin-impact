using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>火山翻译匿名文本合成；不伪造授权，不上传参考音频，不提供伪流式能力。</summary>
public sealed class HuoshanTtsProvider(Func<TimeoutSettings, HttpClient> clientFactory) : ITtsProvider
{
    /// <summary>当前验证过的服务端点；不是用户可配置的资产上传接口。</summary>
    public static Uri Endpoint { get; } = new("https://translate.volcengine.com/crx/tts/v1/");
    /// <summary>只发布本次可以实际验证的中文音色。</summary>
    public static IReadOnlyList<TtsVoiceInfo> Voices { get; } = Array.AsReadOnly(new[]
    {
        new TtsVoiceInfo("zh_female_qingxin", "清新女声 · 中文", "zh"),
        new TtsVoiceInfo("zh_male_zhubo", "男主播 · 中文", "zh"),
        new TtsVoiceInfo("zh_female_zhubo", "女主播 · 中文", "zh")
    });
    /// <summary>退出时取消本实例的 HTTP；普通对白取消不影响下一个请求。</summary>
    private readonly CancellationTokenSource _shutdown = new();
    /// <summary>共同描述从注册表读取。</summary>
    public TtsProviderInfo Info => TtsProviderRegistry.GetInfo("huoshan");
    /// <summary>限制 JSON（含 base64）体积。</summary>
    private const int MaxResponseBytes = 24 * 1024 * 1024;

    /// <summary>只接受已公布音色，不要求 GPT 资产或账号。</summary>
    public void Validate(AppSettings settings)
    {
        if (!Voices.Any(x => x.Id == settings.Provider?.VoiceId))
            throw new VoiceException(VoiceErrorCode.Configuration, "请选择有效的火山中文预设音色。");
        if (settings.Timeouts.ConnectSeconds is < 1 or > 120 || settings.Timeouts.RequestSeconds is < 1 or > 600
            || settings.Timeouts.SynthesisSeconds is < 1 or > 600)
            throw new VoiceException(VoiceErrorCode.Configuration, "连接超时应为 1～120 秒，请求/合成超时应为 1～600 秒。");
    }
    /// <summary>稳定本地预设；不会为刷新列表发送合成请求。</summary>
    public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(Voices); }
    /// <summary>只检查域名可达性；预设和实际合成由独立测试命令验证。</summary>
    public async Task<string> ProbeAsync(AppSettings settings, CancellationToken token)
    {
        using var client = clientFactory(settings.Timeouts);
        client.Timeout = TimeSpan.FromSeconds(settings.Timeouts.RequestSeconds);
        using var response = await client.SendAsync(new(HttpMethod.Head, Endpoint), token).ConfigureAwait(false);
        if ((int)response.StatusCode >= 500 || response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new VoiceException(VoiceErrorCode.Unreachable, $"火山服务不可用：HTTP {(int)response.StatusCode}。");
        return $"火山域名已响应 HTTP {(int)response.StatusCode}；合成请使用测试语音。";
    }
    /// <summary>完整读取有界 JSON/base64/MP3，再用现有 NAudio 解码为 PCM16 WAV。</summary>
    public async Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request,
        IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
    {
        Validate(settings);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 2000)
            throw new VoiceException(VoiceErrorCode.Configuration, "火山合成台词长度必须为 1～2000 字符。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Timeouts.SynthesisSeconds));
        var token = timeout.Token;
        var watch = Stopwatch.StartNew();
        try
        {
            token.ThrowIfCancellationRequested();
            using var client = clientFactory(settings.Timeouts);
            client.Timeout = Timeout.InfiniteTimeSpan;
            progress?.Report(TtsPhase.Synthesizing);
            QuestVoiceLog.Diagnostic.Information("火山请求开始 voice={VoiceId} characters={Characters}", settings.Provider!.VoiceId, request.Text.Length);
            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                { Content = JsonContent.Create(new { text = request.Text, speaker = settings.Provider!.VoiceId }) };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new VoiceException(VoiceErrorCode.SynthesisRejected, $"火山合成被拒绝：HTTP {(int)response.StatusCode}。");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) throw Invalid("火山响应超出大小限制。");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[16384];
            double? firstByte = null;
            int count;
            while ((count = await stream.ReadAsync(block, token).ConfigureAwait(false)) != 0)
            {
                firstByte ??= watch.Elapsed.TotalSeconds;
                if (buffer.Length + count > MaxResponseBytes) throw Invalid("火山响应超出大小限制。");
                buffer.Write(block, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            if (!json.RootElement.TryGetProperty("audio", out var audio)
                || audio.ValueKind != JsonValueKind.Object || !audio.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.String) throw Invalid("火山未返回音频数据。");
            var encoded = data.GetBytesFromBase64();
            var wav = await Task.Run(() => new TtsAudioDecoder().Decode(encoded, TtsAudioEncoding.Mp3, token), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            QuestVoiceLog.Diagnostic.Information("火山完整音频就绪 firstResponseByteSeconds={FirstByte} playableSeconds={Playable} encodedBytes={EncodedBytes} audioSeconds={AudioSeconds}",
                firstByte, watch.Elapsed.TotalSeconds, encoded.Length, wav.DurationSeconds);
            return new(wav.Bytes, watch.Elapsed.TotalSeconds, firstByte ?? watch.Elapsed.TotalSeconds,
                wav.DurationSeconds, wav.SampleRate, wav.Channels, ReferenceFallback.None, TtsAudioEncoding.Mp3);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken.IsCancellationRequested ? cancellationToken : _shutdown.Token); }
        catch (OperationCanceledException exception)
        { throw new VoiceException(VoiceErrorCode.Timeout, "火山合成超时，可重试或切换引擎。", false, exception); }
        catch (HttpRequestException exception)
        { throw new VoiceException(VoiceErrorCode.Unreachable, "无法连接火山翻译，请检查网络或稍后重试。", false, exception); }
        catch (IOException exception)
        { throw new VoiceException(VoiceErrorCode.InterruptedResponse, "火山响应接收中断，可重试。", false, exception); }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        { throw new VoiceException(VoiceErrorCode.InvalidAudio, "火山返回的 JSON 或音频编码无效。", false, exception); }
    }
    /// <summary>诊断错误不携带服务响应中的潜在敏感内容。</summary>
    private static VoiceException Invalid(string detail) => new(VoiceErrorCode.InvalidAudio, detail);
    /// <summary>只取消本地工作，不调用远程控制端点。</summary>
    public void Shutdown() => _shutdown.Cancel();
    /// <summary>调用方须等待在途请求结束后释放。</summary>
    public void Dispose() { _shutdown.Cancel(); _shutdown.Dispose(); }
}
