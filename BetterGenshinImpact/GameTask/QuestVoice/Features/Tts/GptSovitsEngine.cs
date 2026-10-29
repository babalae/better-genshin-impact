// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
/// <summary>GptSovitsEngine：原神自动配音的业务类型。</summary>
public sealed partial class GptSovitsEngine(Func<TimeoutSettings, HttpClient> clientFactory,
    IReferenceAudioSelector selector, IWavProcessor wavProcessor) : IStreamingTtsEngine, ITtsProvider
{
    /// <summary>保护异步操作所有权，确保停止等待已发出的工作。</summary>
    private readonly SemaphoreSlim _ownership = new(1, 1);
    /// <summary>关闭本地 HTTP 连接的取消源。</summary>
    private readonly CancellationTokenSource _shutdown = new();
    /// <summary>标记远端请求状态未知，禁止继续覆盖后端权重。</summary>
    private bool _backendUnknown;

    /// <summary>串行切换声线权重并读取完整语音响应，取消后丢弃结果。</summary>
    public Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request, IProgress<TtsPhase>? progress,
        CancellationToken cancellationToken) => SynthesizeOwnedAsync(settings, request, progress, cancellationToken, null);

    /// <summary>完整与流式共用同一个切权/在途HTTP所有权，Ready不能提前释放它。</summary>
    private async Task<TtsResult> SynthesizeOwnedAsync(AppSettings settings, TtsRequest request, IProgress<TtsPhase>? progress,
        CancellationToken cancellationToken, Action<StreamingPcmAudio>? ready)
    {
        using var queueToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _ownership.WaitAsync(queueToken.Token).ConfigureAwait(false);
        try
        {
            if (_backendUnknown)
                throw new VoiceException(VoiceErrorCode.BackendUnknown, "后端状态未知。请先重启 GPT-SoVITS，再重启本程序。", true);
            if (string.IsNullOrWhiteSpace(request.Text))
                throw new VoiceException(VoiceErrorCode.Configuration, "请输入台词。");
            var reference = selector.Select(request.Voice, request.Emotion);
            Validate(settings);
            await Task.Run(() => ValidateAssets(request.Voice, reference.Audio, settings.Voice.ServerSidePaths), queueToken.Token).ConfigureAwait(false);
            using var client = clientFactory(settings.Timeouts);
            client.BaseAddress = settings.TtsServiceUrl;
            client.Timeout = Timeout.InfiniteTimeSpan;
            queueToken.Token.ThrowIfCancellationRequested();
            // User cancellation discards output, but never cancels an issued HTTP request.
            // This registration lives inside the serial ownership, including body drainage.
            using var registration = cancellationToken.Register(() => progress?.Report(TtsPhase.Draining));
            progress?.Report(TtsPhase.SwitchingWeights);
            queueToken.Token.ThrowIfCancellationRequested();
            await SetWeightsAsync(client, "set_gpt_weights", request.Voice.GptWeightsPath, settings.Timeouts).ConfigureAwait(false);
            queueToken.Token.ThrowIfCancellationRequested();
            await SetWeightsAsync(client, "set_sovits_weights", request.Voice.SovitsWeightsPath, settings.Timeouts).ConfigureAwait(false);
            queueToken.Token.ThrowIfCancellationRequested();
            progress?.Report(TtsPhase.Synthesizing);
            queueToken.Token.ThrowIfCancellationRequested();
            var result = ready is null
                ? await ReadSynthesisAsync(client, settings, request, reference).ConfigureAwait(false)
                : await ReadStreamingAsync(client, settings, request, reference, ready, cancellationToken).ConfigureAwait(false);
            queueToken.Token.ThrowIfCancellationRequested();
            var audio = wavProcessor.ValidateCompletedResponse(result.Bytes);
            return new(audio.Bytes, result.Elapsed, result.FirstByte, audio.DurationSeconds,
                audio.SampleRate, audio.Channels, reference.Fallback);
        }
        catch (VoiceException exception) when (exception.BackendUnknown)
        {
            _backendUnknown = true;
            throw;
        }
        finally { _ownership.Release(); }
    }

    /// <summary>请求 GPT-SoVITS 切换权重，必须收到 success 确认。</summary>
    private async Task SetWeightsAsync(HttpClient client, string endpoint, string path, TimeoutSettings timeouts)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeouts.RequestSeconds));
        try
        {
            using var response = await client.GetAsync(endpoint + "?weights_path=" + Uri.EscapeDataString(path),
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new VoiceException(VoiceErrorCode.WeightRejected, $"切换权重被拒绝：HTTP {(int)response.StatusCode}，{body}");
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind != JsonValueKind.Object
                    || !json.RootElement.TryGetProperty("message", out var message)
                    || message.ValueKind != JsonValueKind.String || message.GetString() != "success")
                    throw new JsonException("Missing success acknowledgement");
            }
            catch (JsonException exception)
            {
                throw new VoiceException(VoiceErrorCode.WeightRejected, "切换权重没有返回 success 确认。", false, exception);
            }
        }
        catch (OperationCanceledException exception) { throw TimeoutError(exception); }
        catch (HttpRequestException exception) { throw TransportError(exception); }
        catch (IOException exception) { throw Interrupted(exception); }
    }

    /// <summary>读取 HTTP 响应至正常 EOF，禁止接受截断音频。</summary>
    private async Task<(byte[] Bytes, double Elapsed, double FirstByte)> ReadSynthesisAsync(HttpClient client,
        AppSettings settings, TtsRequest request, ReferenceSelection reference)
    {
        var protocol = settings.Protocol ?? new TtsProtocolSettings();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Timeouts.SynthesisSeconds));
        var watch = Stopwatch.StartNew();
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "tts")
            {
                Content = JsonContent.Create(new
                {
                    text = request.Text, text_lang = protocol.TextLanguage,
                    ref_audio_path = reference.Audio.Path, prompt_text = reference.Audio.PromptText,
                    prompt_lang = protocol.PromptLanguage, media_type = protocol.MediaType,
                    text_split_method = protocol.TextSplitMethod, streaming_mode = protocol.StreamingMode
                })
            };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                throw new VoiceException(VoiceErrorCode.SynthesisRejected, $"合成被拒绝：HTTP {(int)response.StatusCode}，{body}");
            }
            await using var bodyStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[16384];
            double? firstByte = null;
            while (true)
            {
                var count = await bodyStream.ReadAsync(block, timeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                firstByte ??= watch.Elapsed.TotalSeconds;
                if (buffer.Length + count > 32 * 1024 * 1024)
                    throw new VoiceException(VoiceErrorCode.InvalidAudio, "完整 WAV 超过32MiB预算；已关闭本地连接，后端状态未知。", true);
                buffer.Write(block, 0, count);
            }
            var elapsed = watch.Elapsed.TotalSeconds;
            // Only normal EOF returns bytes. The ownership scope checks user cancellation
            // before validating or producing an accepted result.
            return (buffer.ToArray(), elapsed, firstByte ?? elapsed);
        }
        catch (OperationCanceledException exception) { throw TimeoutError(exception); }
        catch (HttpRequestException exception) { throw TransportError(exception); }
        catch (IOException exception) { throw Interrupted(exception); }
    }

    /// <summary>合成前确认权重、参考音频和逐字参考文本存在。</summary>
    private static void ValidateAssets(VoiceBinding voice, ReferenceAudio reference, bool serverSidePaths)
    {
        foreach (var path in new[] { voice.GptWeightsPath, voice.SovitsWeightsPath, reference.Path })
            if (string.IsNullOrWhiteSpace(path) || (!serverSidePaths && !File.Exists(path)))
                throw new VoiceException(VoiceErrorCode.Configuration, $"资产路径缺失或本机文件不存在：{path}");
        if (string.IsNullOrWhiteSpace(reference.PromptText))
            throw new VoiceException(VoiceErrorCode.Configuration, "参考音频文本不能为空。");
    }

    /// <summary>把超时或本地关闭映射为后端状态未知的错误。</summary>
    private VoiceException TimeoutError(OperationCanceledException exception) => _shutdown.IsCancellationRequested
        ? new(VoiceErrorCode.Cancelled, "本地已关闭连接；无法确认远端任务结束。", true, exception)
        : new(VoiceErrorCode.Timeout, "等待后端超时；后端状态未知。", true, exception);
    /// <summary>区分连接不可达与响应中断。</summary>
    private static VoiceException TransportError(HttpRequestException exception)
    {
        if (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.HostNotFound })
            return new(VoiceErrorCode.Unreachable, "无法连接 GPT-SoVITS，请检查服务地址和服务进程。", false, exception);
        return Interrupted(exception);
    }
    /// <summary>构造响应中断且远端状态未知的错误。</summary>
    private static VoiceException Interrupted(Exception exception) => new(VoiceErrorCode.InterruptedResponse,
        "后端响应中断，无法确认完整音频或远端任务结束；后端状态未知。", true, exception);
    /// <summary>中断本地 HTTP 等待，不向后端发送关闭指令。</summary>
    public void Shutdown() => _shutdown.Cancel();
    /// <summary>释放本实例拥有的本地资源。</summary>
    public void Dispose() => Shutdown();
}
