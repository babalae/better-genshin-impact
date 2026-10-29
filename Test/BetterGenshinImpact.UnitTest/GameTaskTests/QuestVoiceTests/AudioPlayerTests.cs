using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using NAudio.Wave;
using Xunit;
namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;
/// <summary>移植回归验证：AudioPlayerTests。</summary>
public sealed class AudioPlayerTests
{
    /// <summary>移植回归验证：Already cancelled playback never opens a device。</summary>
    [Fact]
    public async Task Already_cancelled_playback_never_opens_a_device()
    {
        var player = new WasapiAudioPlayer(() => new WasapiOut());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.PlayAsync([], cancelled.Token));
    }
    /// <summary>移植回归验证：Malformed wave does not escape as unreachable backend。</summary>
    [Fact]
    public async Task Malformed_wave_does_not_escape_as_unreachable_backend()
    {
        var player = new WasapiAudioPlayer(() => new WasapiOut());
        var error = await Assert.ThrowsAsync<VoiceException>(() => player.PlayAsync([1, 2, 3], default));
        Assert.Equal(VoiceErrorCode.PlaybackDevice, error.Code);
    }

    /// <summary>移植回归验证：Device failure during play does not leave a disposed player for stop or close。</summary>
    [Fact]
    public async Task Device_failure_during_play_does_not_leave_a_disposed_player_for_stop_or_close()
    {
        var device = new FailingDevice();
        using var player = new WasapiAudioPlayer(() => device);
        var wav = new WavProcessor().ValidateCompletedResponse(WavAndReferenceTests.Wave(32000, [300, -300])).Bytes;
        var error = await Assert.ThrowsAsync<VoiceException>(() => player.PlayAsync(wav, default));
        Assert.Equal(VoiceErrorCode.PlaybackDevice, error.Code);
        Assert.True(device.Disposed);
        player.Stop();
    }

    /// <summary>移植回归验证：FailingDevice。</summary>
    private sealed class FailingDevice : IWavePlayer
    {
        /// <summary>移植回归验证：Disposed。</summary>
        public bool Disposed { get; private set; }
        /// <summary>移植回归验证：Volume。</summary>
        public float Volume { get; set; }
        /// <summary>移植回归验证：PlaybackState。</summary>
        public PlaybackState PlaybackState => PlaybackState.Stopped;
        /// <summary>移植回归验证：OutputWaveFormat。</summary>
        public WaveFormat OutputWaveFormat => new(32000, 16, 1);
        /// <summary>移植回归验证：PlaybackStopped。</summary>
        public event EventHandler<StoppedEventArgs> PlaybackStopped { add { } remove { } }
        /// <summary>移植回归验证：Init。</summary>
        public void Init(IWaveProvider provider) { }
        /// <summary>移植回归验证：Play。</summary>
        public void Play() => throw new InvalidOperationException("Device vanished during Play");
        /// <summary>请求当前输出设备停止。</summary>
        public void Stop() { if (Disposed) throw new ObjectDisposedException(nameof(FailingDevice)); }
        /// <summary>移植回归验证：Pause。</summary>
        public void Pause() { }
        /// <summary>释放本实例拥有的本地资源。</summary>
        public void Dispose() => Disposed = true;
    }
}
