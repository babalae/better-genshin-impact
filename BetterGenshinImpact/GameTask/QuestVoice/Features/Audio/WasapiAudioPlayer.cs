// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using NAudio.Wave;
using NAudio;
using System.Runtime.InteropServices;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
/// <summary>WasapiAudioPlayer：原神自动配音的业务类型。</summary>
public sealed class WasapiAudioPlayer(Func<IWavePlayer> outputFactory) : IStreamingAudioPlayer, IDisposable
{
    /// <summary>保护会话状态的同步锁。</summary>
    private readonly object _gate = new();
    /// <summary>当前 WASAPI 共享输出设备。</summary>
    private IWavePlayer? _output;

    /// <summary>在一个输出设备生命周期中持续读取同一PCM源。</summary>
    public Task PlayStreamingAsync(StreamingPcmAudio source, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var reader = source.CreateReader();
                using var output = outputFactory();
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                output.PlaybackStopped += (_, args) =>
                {
                    if (args.Exception is null) finished.TrySetResult();
                    else finished.TrySetException(args.Exception);
                };
                output.Init(reader);
                using var registration = cancellationToken.Register(Stop);
                try
                {
                    lock (_gate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _output = output; output.Play();
                    }
                    await finished.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                finally { lock (_gate) _output = null; }
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException
                or ArgumentException or FormatException or IOException or MmException)
            {
                throw new VoiceException(VoiceErrorCode.PlaybackDevice, "WASAPI流式播放失败：" + exception.Message, false, exception);
            }
            finally
            {
                QuestVoiceLog.Diagnostic.Information("流式播放结束 source={SourceId} underruns={Underruns} insertedSilenceBytes={SilenceBytes}",
                    source.Id, source.UnderrunEpisodes, source.UnderrunSilenceBytes);
            }
        }, cancellationToken);

    /// <summary>在工作线程使用默认共享输出播放完整 WAV，可取消并等待实际结束。</summary>
    public Task PlayAsync(byte[] wav, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var memory = new MemoryStream(wav, writable: false);
            using var reader = new WaveFileReader(memory);
            // NAudio's default constructor opens the system default render endpoint in
            // shared mode. Construction on this worker avoids a UI synchronization context.
            using var output = outputFactory();
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is null) finished.TrySetResult();
                else finished.TrySetException(args.Exception);
            };
            output.Init(reader);
            using var registration = cancellationToken.Register(Stop);
            try
            {
                lock (_gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _output = output;
                    output.Play();
                }
                await finished.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                lock (_gate) _output = null;
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
            or ArgumentException or FormatException or IOException or MmException)
        {
            throw new VoiceException(VoiceErrorCode.PlaybackDevice, "WASAPI 播放失败，请检查默认输出设备：" + exception.Message, false, exception);
        }
    }, cancellationToken);

    /// <summary>请求当前输出设备停止。</summary>
    public void Stop()
    {
        lock (_gate) _output?.Stop();
    }
    /// <summary>释放本实例拥有的本地资源。</summary>
    public void Dispose() => Stop();
}
