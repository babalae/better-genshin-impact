using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>普通 HTTP 语音的有界接收与生命周期；不参与 GPT 排空或增量播放。</summary>
public abstract class HttpSpeechProvider(Func<TimeoutSettings, HttpClient> clientFactory) : ITtsProvider
{
    /// <summary>本实例退出取消源；逐句取消不污染下一句。</summary>
    private readonly CancellationTokenSource _shutdown = new();
    /// <summary>计时属于异步请求上下文，同一实例并发请求不会互相覆盖。</summary>
    private static readonly AsyncLocal<ResponseTiming?> Timing = new();
    /// <summary>区分首响应字节和完整解码就绪；JSON查询首字节也只代表HTTP响应。</summary>
    private sealed class ResponseTiming
    {
        /// <summary>本次合成计时器。</summary>
        public Stopwatch Watch { get; } = Stopwatch.StartNew();
        /// <summary>第一次从任意协议响应读取到数据的时间。</summary>
        public double? FirstByte { get; set; }
    }
    /// <summary>实际引擎能力由派生类公开。</summary>
    public abstract TtsProviderInfo Info { get; }
    /// <summary>验证声音和专属参数。</summary>
    protected abstract void ValidateVoice(AppSettings settings);
    /// <summary>获取引擎声音，初始化时不主动联网。</summary>
    protected abstract Task<IReadOnlyList<TtsVoiceInfo>> ReadVoicesAsync(AppSettings settings, HttpClient client, CancellationToken token);
    /// <summary>引擎只负责自身协议，返回完整且正规化的音频。</summary>
    protected abstract Task<(WavAudio Audio, TtsAudioEncoding Encoding)> ReadSpeechAsync(AppSettings settings, TtsRequest request, HttpClient client, CancellationToken token);

    /// <summary>拒绝带凭据、查询或片段的根地址，密钥只允许请求头。</summary>
    protected void ValidateConnection(AppSettings settings)
    {
        var uri = settings.TtsServiceUrl;
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw Configuration("服务地址必须为 HTTP/HTTPS 根路径，不能包含凭据、查询或片段。");
        if (settings.Timeouts.ConnectSeconds is < 1 or > 120 || settings.Timeouts.RequestSeconds is < 1 or > 600
            || settings.Timeouts.SynthesisSeconds is < 1 or > 600) throw Configuration("超时参数超出允许范围。");
    }
    /// <summary>统一连接校验后交给各引擎验证声音参数。</summary>
    public void Validate(AppSettings settings) { ValidateConnection(settings); ValidateVoice(settings); }
    /// <summary>枚举不要求先选中有效声音。</summary>
    public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token)
    {
        ValidateConnection(settings);
        return ExecuteAsync(settings, false, token, (client, ct) => ReadVoicesAsync(settings, client, ct));
    }
    /// <summary>只读声音枚举成功不等同于推理成功。</summary>
    public virtual async Task<string> ProbeAsync(AppSettings settings, CancellationToken token)
    {
        var voices = await GetVoicesAsync(settings, token).ConfigureAwait(false);
        return $"{Info.DisplayName}：可用声音 {voices.Count} 项；实际合成请测试语音。";
    }
    /// <summary>仅验证服务能响应，不将静态建议列表冒充在线声音发现。</summary>
    protected Task CheckEndpointAsync(AppSettings settings, Uri endpoint, CancellationToken token)
        => ExecuteAsync(settings, false, token, async (client, ct) =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Head, endpoint);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode >= 500 || (int)response.StatusCode is 401 or 403)
                throw new VoiceException(VoiceErrorCode.Unreachable, $"语音服务探测失败：HTTP {(int)response.StatusCode}。");
            return true;
        });
    /// <summary>普通完整音频合成，不暴露伪流式能力。</summary>
    public Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request, IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
    {
        Validate(settings);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 4000) throw Configuration("台词长度必须为 1～4000 字符。");
        return ExecuteAsync(settings, true, cancellationToken, async (client, token) =>
        {
            var timing = new ResponseTiming();
            Timing.Value = timing;
            try
            {
                progress?.Report(TtsPhase.Synthesizing);
                var result = await ReadSpeechAsync(settings, request, client, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return new TtsResult(result.Audio.Bytes, timing.Watch.Elapsed.TotalSeconds, timing.FirstByte ?? timing.Watch.Elapsed.TotalSeconds,
                    result.Audio.DurationSeconds, result.Audio.SampleRate, result.Audio.Channels, ReferenceFallback.None, result.Encoding);
            }
            finally { Timing.Value = null; }
        });
    }
    /// <summary>覆盖网络、接收与解码全过程的超时；错误不含原始响应或URL。</summary>
    private async Task<T> ExecuteAsync<T>(AppSettings settings, bool synthesis, CancellationToken cancellation, Func<HttpClient, CancellationToken, Task<T>> operation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(synthesis ? settings.Timeouts.SynthesisSeconds : settings.Timeouts.RequestSeconds));
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            using var client = clientFactory(settings.Timeouts);
            client.Timeout = TimeSpan.FromSeconds(settings.Timeouts.RequestSeconds);
            return await operation(client, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || _shutdown.IsCancellationRequested)
        { throw new OperationCanceledException(cancellation.IsCancellationRequested ? cancellation : _shutdown.Token); }
        catch (OperationCanceledException) { throw new VoiceException(VoiceErrorCode.Timeout, "语音请求超时，可重试或切换引擎。"); }
        catch (HttpRequestException) { throw new VoiceException(VoiceErrorCode.Unreachable, "语音服务连接失败，请检查地址和网络。"); }
        catch (IOException) { throw new VoiceException(VoiceErrorCode.InterruptedResponse, "语音响应接收中断。"); }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw new VoiceException(VoiceErrorCode.InvalidAudio, "语音服务响应结构或音频格式无效。"); }
    }
    /// <summary>每个请求保留自己的message/response/stream所有权，限制长度声明及真实字节数。</summary>
    protected static async Task<byte[]> SendAsync(HttpClient client, HttpRequestMessage message, CancellationToken token, int limit = TtsAudioDecoder.MaxEncodedBytes)
    {
        // ResponseHeadersRead 会提前结束 HttpClient 自带期限，单独覆盖后续音频体读取。
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        requestTimeout.CancelAfter(client.Timeout);
        token = requestTimeout.Token;
        using (message)
        using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
                throw new VoiceException(VoiceErrorCode.SynthesisRejected, $"语音服务拒绝请求：HTTP {(int)response.StatusCode}，请检查服务权限或参数。");
            if (response.Content.Headers.ContentLength > limit) throw Invalid("语音响应超出大小限制。");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var block = new byte[16384];
            int count;
            while ((count = await input.ReadAsync(block, token).ConfigureAwait(false)) != 0)
            {
                if (Timing.Value is { } timing) timing.FirstByte ??= timing.Watch.Elapsed.TotalSeconds;
                if (output.Length + count > limit) throw Invalid("语音响应超出大小限制。");
                output.Write(block, 0, count);
            }
            return output.ToArray();
        }
    }
    /// <summary>有界JSON用于声音列表及查询，不接受无限制服务端数据。</summary>
    protected static async Task<JsonDocument> JsonAsync(HttpClient client, HttpRequestMessage message, CancellationToken token)
        => JsonDocument.Parse(await SendAsync(client, message, token, 2 * 1024 * 1024).ConfigureAwait(false));
    /// <summary>保留用户指定代理路径。</summary>
    protected static Uri Endpoint(AppSettings settings, string path) => new(settings.TtsServiceUrl, path);
    /// <summary>明确解码在后台线程执行，取消贯穿解码。</summary>
    protected static Task<WavAudio> DecodeAsync(byte[] bytes, TtsAudioEncoding encoding, CancellationToken token)
        => Task.Run(() => new TtsAudioDecoder().Decode(bytes, encoding, token), token);
    /// <summary>配置错误不影响其他引擎。</summary>
    protected static VoiceException Configuration(string message) => new(VoiceErrorCode.Configuration, message);
    /// <summary>格式错误不能将后端标记为GPT Unknown。</summary>
    protected static VoiceException Invalid(string message) => new(VoiceErrorCode.InvalidAudio, message);
    /// <summary>退出只取消本地操作。</summary>
    public void Shutdown() => _shutdown.Cancel();
    /// <summary>调用方等待在途操作结束后释放。</summary>
    public void Dispose() { _shutdown.Cancel(); _shutdown.Dispose(); }
}
