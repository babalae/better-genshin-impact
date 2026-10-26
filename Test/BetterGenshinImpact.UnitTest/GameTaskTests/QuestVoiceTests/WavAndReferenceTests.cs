using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：WavAndReferenceTests。</summary>
public sealed class WavAndReferenceTests
{
    /// <summary>移植回归验证：Real stream header is repaired using observed rate and frames。</summary>
    [Fact]
    public void Real_stream_header_is_repaired_using_observed_rate_and_frames()
    {
        var bytes = Wave(24000, [200, -200, 500, -500]);
        var result = new WavProcessor().ValidateCompletedResponse(bytes);
        Assert.Equal(24000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.Equal(4d / 24000, result.DurationSeconds);
        Assert.Equal(44u, BinaryPrimitives.ReadUInt32LittleEndian(result.Bytes.AsSpan(4)));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(result.Bytes.AsSpan(40)));
    }

    /// <summary>移植回归验证：Invalid audio never becomes a success。</summary>
    [Theory]
    [InlineData("silent")]
    [InlineData("empty")]
    [InlineData("partial")]
    [InlineData("json")]
    [InlineData("length")]
    [InlineData("format")]
    [InlineData("rate-overflow")]
    public void Invalid_audio_never_becomes_a_success(string kind)
    {
        var bytes = kind switch
        {
            "silent" => Wave(16000, [0, 0, 0]),
            "empty" => Wave(16000, []),
            "json" => "{\"error\":\"failed\"}"u8.ToArray(),
            _ => Wave(32000, [100, -100])
        };
        if (kind == "partial") bytes = bytes[..^1];
        if (kind == "length") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 100);
        if (kind == "format") bytes[20] = 3;
        if (kind == "rate-overflow") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), uint.MaxValue);
        var error = Assert.Throws<VoiceException>(() => new WavProcessor().ValidateCompletedResponse(bytes));
        Assert.Equal(VoiceErrorCode.InvalidAudio, error.Code);
    }

    /// <summary>移植回归验证：Flat reference works and emotion fallbacks remain distinct。</summary>
    [Fact]
    public void Flat_reference_works_and_emotion_fallbacks_remain_distinct()
    {
        var plain = new ReferenceAudio("reference.wav", "你好。");
        var tagged = new ReferenceAudio("happy.wav", "你好呀。", "happy");
        var selector = new ReferenceAudioSelector();
        var voice = new VoiceBinding("甘雨", "ganyu.ckpt", "ganyu.pth", [plain]);
        Assert.Equal(ReferenceFallback.None, selector.Select(voice, null).Fallback);
        Assert.Equal(ReferenceFallback.CharacterHasNoEmotionTags, selector.Select(voice, "sad").Fallback);
        voice = voice with { References = [plain, tagged] };
        Assert.Equal(ReferenceFallback.EmotionNotAvailable, selector.Select(voice, "sad").Fallback);
        Assert.Same(tagged, selector.Select(voice, "happy").Audio);
    }

    /// <summary>移植回归验证：Every error code requires an explicit owner。</summary>
    [Fact]
    public void Every_error_code_requires_an_explicit_owner()
    {
        foreach (var code in Enum.GetValues<VoiceErrorCode>())
            Assert.True(Enum.IsDefined(VoiceErrors.Owner(code)));
        Assert.Throws<ArgumentOutOfRangeException>(() => VoiceErrors.Owner((VoiceErrorCode)int.MaxValue));
        Assert.Equal(VoiceErrorOwner.Client, VoiceErrors.Owner(VoiceErrorCode.Internal));
        Assert.Equal(VoiceErrorOwner.Client, VoiceErrors.Owner(VoiceErrorCode.Configuration));
        Assert.Equal(VoiceErrorOwner.Backend, VoiceErrors.Owner(VoiceErrorCode.Unreachable));
    }

    /// <summary>移植回归验证：Wave。</summary>
    internal static byte[] Wave(int rate, short[] samples)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8); writer.Write(36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(0);
        foreach (var sample in samples) writer.Write(sample);
        return memory.ToArray();
    }
}
