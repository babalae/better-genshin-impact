using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>在同一个HTTP串行所有权内增加增量PCM接收。</summary>
public sealed partial class GptSovitsEngine
{
    /// <summary>首段就绪后继续持有HTTP；正常EOF才返回完整结果。</summary>
    public Task<TtsResult> SynthesizeStreamingAsync(AppSettings settings, TtsRequest request,
        Action<StreamingPcmAudio> ready, IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
        => SynthesizeOwnedAsync(settings, request, progress, cancellationToken, ready);

    /// <summary>增量消费单头WAV和PCM；首段之后仍读取至EOF，不受用户候选取消影响。</summary>
    private async Task<(byte[] Bytes, double Elapsed, double FirstByte)> ReadStreamingAsync(HttpClient client,
        AppSettings settings, TtsRequest request, ReferenceSelection reference, Action<StreamingPcmAudio> ready,
        CancellationToken userCancellation)
    {
        var protocol = settings.Protocol ?? new TtsProtocolSettings();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Timeouts.SynthesisSeconds));
        var watch = Stopwatch.StartNew();
        Stream? body = null; StreamingPcmAudio? source = null;
        HttpResponseMessage? response = null; var normalEof = false; var published = false;
        double? firstByte = null, firstPcm = null, playable = null;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "tts")
            {
                Content = JsonContent.Create(new
                {
                    text = request.Text, text_lang = protocol.TextLanguage,
                    ref_audio_path = reference.Audio.Path, prompt_text = reference.Audio.PromptText,
                    prompt_lang = protocol.PromptLanguage, media_type = "wav",
                    text_split_method = protocol.TextSplitMethod, streaming_mode = 1,
                    parallel_infer = false, split_bucket = false, batch_size = 1, fragment_interval = .3
                })
            };
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new VoiceException(VoiceErrorCode.SynthesisRejected, $"流式合成被拒绝：HTTP {(int)response.StatusCode}。");
            if (response.Content.Headers.ContentType?.MediaType is not ("audio/wav" or "audio/x-wav" or "audio/wave"))
                throw new VoiceException(VoiceErrorCode.InvalidAudio, "流式后端未返回PCM16 WAV音频响应。");

            var format = await StreamingWaveProtocol.ReadAsync(body, timeout.Token,
                () => firstByte = watch.Elapsed.TotalSeconds).ConfigureAwait(false);
            if (format.DataBytes > 32 * 1024 * 1024)
                throw new VoiceException(VoiceErrorCode.InvalidAudio, "流式WAV声明超过32MiB预算。");
            source = new(settings.Output.AudioDirectory, format.SampleRate, format.Channels);
            var aligner = new StreamingPcmAligner(format.BlockAlign);
            var guard = new StreamingHeaderGuard(); var block = new byte[16384]; long dataBytes = 0;
            async Task AppendAsync(byte[] bytes)
            {
                var pcm = aligner.Push(bytes);
                if (pcm.Length == 0) return;
                firstPcm ??= watch.Elapsed.TotalSeconds;
                await source.AppendAsync(pcm, timeout.Token).ConfigureAwait(false);
                PublishReady();
            }
            void PublishReady()
            {
                if (published || !source.IsReady || userCancellation.IsCancellationRequested) return;
                published = true; playable = watch.Elapsed.TotalSeconds;
                ready(source);
            }
            while (true)
            {
                var count = await body.ReadAsync(block, timeout.Token).ConfigureAwait(false);
                if (count == 0) { normalEof = true; break; }
                dataBytes += count;
                if (dataBytes > 32 * 1024 * 1024)
                    throw new VoiceException(VoiceErrorCode.InvalidAudio, "流式PCM响应超过32MiB预算。");
                await AppendAsync(guard.Push(block.AsSpan(0, count))).ConfigureAwait(false);
            }
            await AppendAsync(guard.Complete()).ConfigureAwait(false); aligner.Complete();
            if (format.DataBytes is { } expected && expected != dataBytes)
                throw new VoiceException(VoiceErrorCode.InvalidAudio, "流式WAV声明长度与正常EOF收到的PCM不一致。");
            var audio = wavProcessor.ValidateCompletedResponse(source.ToWav());
            source.Complete(); PublishReady();
            var elapsed = watch.Elapsed.TotalSeconds;
            QuestVoiceLog.Diagnostic.Information("流式TTS mode=1 source={SourceId} firstPCM={FirstPcmSeconds:F3}s ready={ReadySeconds}s eof={EofSeconds:F3}s bytes={Bytes}",
                source.Id, firstPcm, playable, elapsed, source.PcmBytes);
            return (audio.Bytes, elapsed, firstByte ?? elapsed);
        }
        catch (Exception exception)
        {
            // 只有本地格式/消费者错误可以继续尝试排空；传输断流后来Read=0也不能修复Unknown。
            var local = exception is VoiceException { BackendUnknown: false } or EndOfStreamException
                || exception is not (OperationCanceledException or HttpRequestException or IOException);
            if (!normalEof && body is not null && local)
            {
                try
                {
                    var discard = new byte[16384];
                    while (await body.ReadAsync(discard, timeout.Token).ConfigureAwait(false) != 0) { }
                    normalEof = true;
                }
                catch (Exception drain) { exception = drain; }
            }
            var failure = exception switch
            {
                OperationCanceledException cancelled => TimeoutError(cancelled),
                HttpRequestException transport => TransportError(transport),
                VoiceException voice when normalEof || voice.BackendUnknown => voice,
                EndOfStreamException when normalEof => new VoiceException(VoiceErrorCode.InvalidAudio, "流式WAV头或PCM帧截断。", false, exception),
                _ when !normalEof => Interrupted(exception),
                _ => new VoiceException(VoiceErrorCode.InvalidAudio, "流式音频处理失败：" + exception.Message, false, exception)
            };
            source?.Fail(failure);
            throw failure;
        }
        finally { response?.Dispose(); source?.Dispose(); }
    }
}
