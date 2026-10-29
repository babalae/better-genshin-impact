using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using NAudio.Wave;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>替代设备验证真实播放器生命周期，不打开用户声卡。</summary>
public sealed class StreamingPlayerTests : IDisposable
{
    /// <summary>本例独立的PCM临时目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "QuestVoiceStreamingPlayer", Guid.NewGuid().ToString("N"));

    /// <summary>预先取消不能打开音频设备。</summary>
    [Fact]
    public async Task AlreadyCancelledNeverCreatesDevice()
    {
        using var source = new StreamingPcmAudio(_root, 32000, 1); var created = 0;
        using var player = new WasapiAudioPlayer(() => { created++; return new Device(); });
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.PlayStreamingAsync(source, cancel.Token));
        Assert.Equal(0, created);
    }

    /// <summary>后续PCM持续送入同一设备，不重复创建/初始化/播放。</summary>
    [Fact]
    public async Task IncrementalAppendsUseOneDeviceAndPreserveExactPcm()
    {
        using var source = new StreamingPcmAudio(_root, 32000, 1); using var device = new Device();
        var created = 0; using var player = new WasapiAudioPlayer(() => { created++; return device; });
        var first = Enumerable.Repeat((byte)7, 12800).ToArray(); var second = Enumerable.Repeat((byte)8, 12800).ToArray();
        await source.AppendAsync(first, default);
        var playback = player.PlayStreamingAsync(source, default);
        await device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(playback.IsCompleted);
        await source.AppendAsync(second, default); source.Complete(); device.Continue.TrySetResult(true);
        await playback.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(first.Concat(second).ToArray(), device.Submitted.ToArray());
        Assert.Equal(1, created); Assert.Equal(1, device.Initializations); Assert.Equal(1, device.Plays);
        Assert.True(device.Disposed); Assert.Equal(0, source.UnderrunEpisodes);
        player.Stop();
    }

    /// <summary>本地取消释放设备，但不把源当成后端已完成。</summary>
    [Fact]
    public async Task CancellationStopsAndDisposesDeviceWhileProducerRemainsUsable()
    {
        using var source = new StreamingPcmAudio(_root, 32000, 1); using var device = new Device();
        using var player = new WasapiAudioPlayer(() => device); using var cancel = new CancellationTokenSource();
        await source.AppendAsync(new byte[12800], default);
        var playback = player.PlayStreamingAsync(source, cancel.Token);
        await device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(device.Disposed); Assert.True(device.Stops > 0);
        await source.AppendAsync(new byte[] { 3, 0 }, default); source.Complete();
        Assert.Equal(12802, source.PcmBytes);
    }

    /// <summary>仅清理自己创建的测试目录。</summary>
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    /// <summary>顺序读取真实provider，首读后暂停让测试追加后续PCM。</summary>
    private sealed class Device : IWavePlayer
    {
        /// <summary>第一次消费输入PCM的信号。</summary>
        public TaskCompletionSource<bool> FirstRead { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>继续消费或停止等待。</summary>
        public TaskCompletionSource<bool> Continue { get; } = ContinuousVoiceFixture.NewCompletion<bool>();
        /// <summary>保存设备实际读入数据以校验不丢不重。</summary>
        public MemoryStream Submitted { get; } = new();
        /// <summary>本次设备初始化次数。</summary>
        public int Initializations { get; private set; }
        /// <summary>开始播放次数。</summary>
        public int Plays { get; private set; }
        /// <summary>收到停止次数。</summary>
        public int Stops { get; private set; }
        /// <summary>已释放标记。</summary>
        public bool Disposed { get; private set; }
        /// <summary>仅测试NAudio契约，不调整系统音量。</summary>
        public float Volume { get; set; }
        /// <summary>设备读入PCM的播放状态。</summary>
        public PlaybackState PlaybackState { get; private set; }
        /// <summary>设备输出格式沿用源格式。</summary>
        public WaveFormat OutputWaveFormat => _provider!.WaveFormat;
        /// <summary>设备的真实结束信号。</summary>
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        /// <summary>本次唯一输入源。</summary>
        private IWaveProvider? _provider;
        /// <summary>初始化唯一provider。</summary>
        public void Init(IWaveProvider provider) { Initializations++; _provider = provider; }
        /// <summary>异步顺序读取，错误通过PlaybackStopped返回。</summary>
        public void Play()
        {
            Plays++; PlaybackState = PlaybackState.Playing;
            _ = Task.Run(async () =>
            {
                Exception? failure = null;
                try
                {
                    var bytes = new byte[4096]; var count = _provider!.Read(bytes, 0, bytes.Length);
                    Submitted.Write(bytes, 0, count); FirstRead.TrySetResult(true); await Continue.Task;
                    while (PlaybackState == PlaybackState.Playing && (count = _provider.Read(bytes, 0, bytes.Length)) != 0)
                        Submitted.Write(bytes, 0, count);
                }
                catch (Exception error) { failure = error; }
                finally { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new(failure)); }
            });
        }
        /// <summary>停止后唤醒输出任务，由任务发出真正的结束事件。</summary>
        public void Stop() { Stops++; PlaybackState = PlaybackState.Stopped; Continue.TrySetResult(false); }
        /// <summary>本测试不使用暂停。</summary>
        public void Pause() => throw new NotSupportedException();
        /// <summary>释放标记用于检查退出后不留悬空输出。</summary>
        public void Dispose() => Disposed = true;
    }
}
