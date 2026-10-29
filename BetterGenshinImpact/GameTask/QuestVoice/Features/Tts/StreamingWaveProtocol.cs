using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>流式单头PCM16 WAV的格式描述，data=0表示读至HTTP正常EOF。</summary>
internal sealed record StreamingPcmFormat(int SampleRate, int Channels, long? DataBytes)
{
    /// <summary>一个完整PCM帧的字节数。</summary>
    public int BlockAlign => Channels * 2;
}

/// <summary>按实际字节解析本地api_v2协议，不能依赖HTTP Read边界。</summary>
internal static class StreamingWaveProtocol
{
    /// <summary>最多64KiB元数据，拒绝格式错误，首部本身不是Ready。</summary>
    public static async Task<StreamingPcmFormat> ReadAsync(Stream body, CancellationToken token, Action? firstByte = null)
    {
        var header = new byte[12]; var first = await body.ReadAsync(header, token).ConfigureAwait(false);
        if (first == 0) throw new EndOfStreamException("流式响应没有WAV头。");
        firstByte?.Invoke();
        await body.ReadExactlyAsync(header.AsMemory(first), token).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw Invalid("流式响应缺少RIFF/WAVE头。");
        StreamingPcmFormat? format = null; var consumed = 12;
        while (consumed < 65536)
        {
            var chunk = new byte[8]; await body.ReadExactlyAsync(chunk, token).ConfigureAwait(false); consumed += 8;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            if (chunk.AsSpan(0, 4).SequenceEqual("data"u8))
            {
                if (format is null || size % format.BlockAlign != 0) throw Invalid("流式data前缺少格式或PCM帧长度不完整。");
                return format with { DataBytes = size == 0 ? null : size };
            }
            if (size > 65536 - consumed) throw Invalid("流式WAV元数据超过64KiB。");
            var bytes = new byte[size + size % 2]; await body.ReadExactlyAsync(bytes, token).ConfigureAwait(false); consumed += bytes.Length;
            if (!chunk.AsSpan(0, 4).SequenceEqual("fmt "u8)) continue;
            if (size < 16 || BinaryPrimitives.ReadInt16LittleEndian(bytes) != 1) throw Invalid("流式只支持PCM16。");
            var channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2));
            var rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            var bits = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(14));
            if (channels is < 1 or > 2 || rate is < 8000 or > 192000 || bits != 16
                || BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(12)) != channels * 2
                || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)) != rate * channels * 2)
                throw Invalid("流式PCM采样率、位深或声道无效。");
            format = new(rate, channels, null);
        }
        throw Invalid("64KiB内没有流式WAV data。");
    }
    /// <summary>格式错误独立于网络断流，允许传输所有者排空后恢复。</summary>
    internal static VoiceException Invalid(string message) => new(VoiceErrorCode.InvalidAudio, message);
}

/// <summary>跨Read保留不足一帧的字节，不丢失也不提前播放它们。</summary>
internal sealed class StreamingPcmAligner(int alignment)
{
    /// <summary>最多一帧减一字节的尾部。</summary>
    private byte[] _tail = [];
    /// <summary>只返回完整PCM帧。</summary>
    public byte[] Push(ReadOnlySpan<byte> bytes)
    {
        var all = new byte[_tail.Length + bytes.Length]; _tail.CopyTo(all, 0); bytes.CopyTo(all.AsSpan(_tail.Length));
        var count = all.Length / alignment * alignment; _tail = all[count..]; return all[..count];
    }
    /// <summary>正常EOF仍有尾字节则音频截断。</summary>
    public void Complete() { if (_tail.Length != 0) throw StreamingWaveProtocol.Invalid("流式PCM末尾不足一个采样帧。"); }
}

/// <summary>不兼容的重复RIFF头会报错，不将文件头当PCM送给声卡。</summary>
internal sealed class StreamingHeaderGuard
{
    /// <summary>跨Read最多保留11字节，用于识别12字节签名。</summary>
    private byte[] _tail = [];
    /// <summary>检测头部签名并返回不再需要跨块检测的字节。</summary>
    public byte[] Push(ReadOnlySpan<byte> bytes)
    {
        var all = new byte[_tail.Length + bytes.Length]; _tail.CopyTo(all, 0); bytes.CopyTo(all.AsSpan(_tail.Length));
        for (var i = 0; i + 12 <= all.Length; i++)
            if (all.AsSpan(i, 4).SequenceEqual("RIFF"u8) && all.AsSpan(i + 8, 4).SequenceEqual("WAVE"u8))
                throw StreamingWaveProtocol.Invalid("PCM中出现重复RIFF/WAVE头，不支持此流式响应格式。");
        var keep = Math.Min(11, all.Length); _tail = all[^keep..]; return all[..(all.Length - keep)];
    }
    /// <summary>EOF归还最后保留的真实PCM字节。</summary>
    public byte[] Complete() { var bytes = _tail; _tail = []; return bytes; }
}
