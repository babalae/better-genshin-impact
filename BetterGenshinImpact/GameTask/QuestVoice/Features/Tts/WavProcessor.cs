// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using System.Buffers.Binary;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
/// <summary>WavProcessor：原神自动配音的业务类型。</summary>
public sealed class WavProcessor : IWavProcessor
{
    // Only the transport's successful EOF path may call this method. A partial stream is
    // indistinguishable from a shorter completed stream when the header declares data=0.
    public WavAudio ValidateCompletedResponse(byte[] bytes)
    {
        if (bytes.Length < 44 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw Invalid("缺少 WAV 首部。");
        var riffSize = Read32(bytes, 4);
        var offset = 12;
        var rate = 0;
        var channels = 0;
        var alignment = 0;
        var dataOffset = -1;
        var dataLength = 0;
        var lengthOffset = 0;
        while (offset <= bytes.Length - 8)
        {
            var size = Read32(bytes, offset + 4);
            if (bytes.AsSpan(offset, 4).SequenceEqual("fmt "u8))
            {
                if (size < 16 || size > bytes.Length - offset - 8) throw Invalid("损坏的格式区块。");
                if (Read16(bytes, offset + 8) != 1 || Read16(bytes, offset + 22) != 16)
                    throw Invalid("仅支持 PCM16 WAV。");
                channels = Read16(bytes, offset + 10);
                var declaredRate = Read32(bytes, offset + 12);
                if (declaredRate is < 8000 or > 192000) throw Invalid("采样率无效。");
                rate = (int)declaredRate;
                alignment = Read16(bytes, offset + 20);
                if (channels is < 1 or > 2 || rate is < 8000 or > 192000
                    || alignment != channels * 2 || Read32(bytes, offset + 16) != rate * alignment)
                    throw Invalid("采样率、声道或帧格式无效。");
            }
            else if (bytes.AsSpan(offset, 4).SequenceEqual("data"u8))
            {
                dataOffset = offset + 8;
                lengthOffset = offset + 4;
                var remaining = bytes.Length - dataOffset;
                if (size == 0 && riffSize == 36 && offset == 36) dataLength = remaining;
                else if (size == remaining) dataLength = remaining;
                else throw Invalid("WAV 声明的长度与完整响应不符。");
                break;
            }
            if (size > bytes.Length - offset - 8) throw Invalid("WAV 区块超出响应长度。");
            offset += checked(8 + (int)size + (int)(size & 1));
        }
        if (dataOffset < 0 || alignment == 0 || dataLength == 0 || dataLength % alignment != 0)
            throw Invalid("音频为空或 PCM 帧不完整。");
        if (riffSize != 36 && riffSize != bytes.Length - 8) throw Invalid("RIFF 长度不符。");
        var nonzero = false;
        for (var i = dataOffset; i < bytes.Length; i += 2)
            nonzero |= BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i, 2)) != 0;
        if (!nonzero) throw Invalid("后端返回了全静音音频。");
        var repaired = bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(repaired.AsSpan(4), (uint)(bytes.Length - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(repaired.AsSpan(lengthOffset), (uint)dataLength);
        return new(repaired, (double)dataLength / alignment / rate, rate, channels);
    }

    /// <summary>以小端读取 WAV 的 32 位字段。</summary>
    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    /// <summary>以小端读取 WAV 的 16 位字段。</summary>
    private static ushort Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    /// <summary>Invalid：执行WavProcessor中的对应校验或计算步骤。</summary>
    private static VoiceException Invalid(string message) => new(VoiceErrorCode.InvalidAudio, message);
}
