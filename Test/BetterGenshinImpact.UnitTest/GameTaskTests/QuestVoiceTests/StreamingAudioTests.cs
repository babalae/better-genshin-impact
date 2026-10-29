using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>验证真正增量PCM源的水位、读序、有限磁盘与所有权。</summary>
public sealed class StreamingAudioTests : IDisposable
{
    /// <summary>仅在本次测试独有目录中写入临时PCM。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "QuestVoiceStreaming", Guid.NewGuid().ToString("N"));

    /// <summary>不足水位或只有静音不能触发新句接替。</summary>
    [Fact]
    public async Task ReadyRequiresTwoHundredMillisecondsAndNonSilentSamples()
    {
        using var audio = new StreamingPcmAudio(_root, 32000, 1);
        Assert.False(audio.IsReady);
        await audio.AppendAsync(new byte[12800], default);
        Assert.False(audio.IsReady);
        await audio.AppendAsync(new byte[] { 1, 0 }, default);
        Assert.True(audio.IsReady);
        audio.Complete();
        Assert.NotEmpty(new WavProcessor().ValidateCompletedResponse(audio.ToWav()).Bytes);
    }

    /// <summary>生产者继续追加，消费者维持一个连续音频输入；正常EOF不补静音。</summary>
    [Fact]
    public async Task GrowingPcmReadsInOrderAndCompletesWithoutExtraSilence()
    {
        using var audio = new StreamingPcmAudio(_root, 32000, 1);
        var pcm = Enumerable.Range(0, 400).Select(i => (byte)i).ToArray();
        await audio.AppendAsync(pcm.AsMemory(0, 200), default);
        using var reader = audio.CreateReader();
        var bytes = new byte[200];
        Assert.Equal(200, reader.Read(bytes, 0, bytes.Length));
        Assert.Equal(pcm[..200], bytes);
        await audio.AppendAsync(pcm.AsMemory(200), default);
        audio.Complete();
        Assert.Equal(200, reader.Read(bytes, 0, bytes.Length));
        Assert.Equal(pcm[200..], bytes);
        Assert.Equal(0, reader.Read(bytes, 0, bytes.Length));
        Assert.Equal(0, audio.UnderrunEpisodes);
    }

    /// <summary>缺料时插零有明确计数，不把短读误当成自然结束。</summary>
    [Fact]
    public async Task UnderrunIsCountedAndDoesNotDiscardLaterPcm()
    {
        using var audio = new StreamingPcmAudio(_root, 32000, 1);
        await audio.AppendAsync(new byte[] { 1, 0 }, default);
        using var reader = audio.CreateReader();
        var bytes = new byte[8];
        Assert.Equal(8, reader.Read(bytes, 0, 8));
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, bytes);
        Assert.Equal(1, audio.UnderrunEpisodes); Assert.Equal(6, audio.UnderrunSilenceBytes);
        await audio.AppendAsync(new byte[] { 2, 0 }, default);
        audio.Complete();
        Assert.Equal(2, reader.Read(bytes, 0, 8));
        Assert.Equal(2, bytes[0]);
        Assert.Equal(0, reader.Read(bytes, 0, 8));
    }

    /// <summary>超过有限spool预算时失败，禁止无限缓存；拒绝不完整PCM帧。</summary>
    [Fact]
    public async Task RejectsOverBudgetAndUnalignedFrames()
    {
        using var audio = new StreamingPcmAudio(_root, 32000, 2, maximumBytes: 8);
        await audio.AppendAsync(new byte[8], default);
        await Assert.ThrowsAsync<VoiceException>(async () => await audio.AppendAsync(new byte[4], default));
        Assert.Equal(8, audio.PcmBytes);
        await Assert.ThrowsAsync<VoiceException>(async () => await audio.AppendAsync(new byte[2], default));
    }

    /// <summary>HTTP所有者释放之后，已接替的播放仍能读到完整数据。</summary>
    [Fact]
    public async Task PlaybackLeaseKeepsSpoolAliveAfterHttpOwnerReleasesIt()
    {
        var audio = new StreamingPcmAudio(_root, 32000, 1);
        await audio.AppendAsync(new byte[] { 4, 0 }, default); audio.Complete();
        audio.Retain(); audio.Dispose();
        using (var reader = audio.CreateReader())
        {
            var bytes = new byte[2]; Assert.Equal(2, reader.Read(bytes, 0, 2)); Assert.Equal(4, bytes[0]);
        }
        audio.Dispose();
        Assert.Empty(Directory.GetFiles(_root));
    }

    /// <summary>传输失败立即让读取失败，不能把剩余部分当正常完整音频。</summary>
    [Fact]
    public async Task FailedSourceCannotBePlayedAsSuccessfulShortAudio()
    {
        using var audio = new StreamingPcmAudio(_root, 32000, 1);
        await audio.AppendAsync(new byte[] { 5, 0 }, default);
        using var reader = audio.CreateReader();
        audio.Fail(new VoiceException(VoiceErrorCode.InterruptedResponse, "断流", true));
        Assert.Throws<VoiceException>(() => reader.Read(new byte[4], 0, 4));
    }

    /// <summary>持续缺料有有限上界，不因后端挂起无限输出静音。</summary>
    [Fact]
    public async Task ContinuousUnderrunStopsWithinDefinedTwoSecondBudget()
    {
        using var audio = new StreamingPcmAudio(_root, 32000, 1); using var reader = audio.CreateReader();
        Assert.Equal(2, reader.Read(new byte[2], 0, 2));
        await Task.Delay(TimeSpan.FromMilliseconds(2100));
        var error = Assert.Throws<VoiceException>(() => reader.Read(new byte[2], 0, 2));
        Assert.Equal(VoiceErrorCode.PlaybackDevice, error.Code);
        Assert.Equal(1, audio.UnderrunEpisodes);
    }

    /// <summary>仅清理本例自己创建的隔离测试目录。</summary>
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
